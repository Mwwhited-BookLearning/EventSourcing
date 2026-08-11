# Feature: Schema registry

Context: full lifecycle in `../05-schema-registry-and-spec-generation.md`
(that doc's own banner notes it's still written in terms of the pre-
`ADR-037` OData listing surface and the pre-`ADR-050` singular claim
fields — this feature doc follows the current mechanism instead, per
`docs/data/schema-registry.md`); entities in `../02-data-model.md` and `../data/schema-registry.md`
(ground truth for this doc's field shapes); per-field indexing in
`../04-odata-filter-pushdown.md`; auth requirements in
[`auth.md`](auth.md). Registration (`PUT /registry/{event-type}`,
`GET /registry/{event-type}`, `GET /registry/{event-type}/{version}`) is
still plain OpenAPI/REST, unaffected by `ADR-037` — only the *listing*
endpoint moved to GraphQL: `eventTypes(first: Int, after: String)` and
`eventType(name: ..., version: ...)`, cursor-paginated fields on the same
Gateway every other query goes through (`ADR-037`, `03-api-contracts.md`
"Registry listing — GraphQL query field"), replacing the old `QUERY
/registry` with OData `$top`/`$skip` (`ADR-012`). `EventTypeDefinition`'s
key is `(AppId, Name, Version)`, not just `(Name, Version)` (`ADR-030`) —
two independent applications can register a type named `OrderPlaced`
with completely different shapes/claims/`ChangeKind` and never collide,
because they're different rows entirely; `AppId` is a required field on
the registration request body itself
(`RegisterEventTypeRequest.AppId`), checked against — never resolved
from — the caller's `registry:admin:{appId}` scope (or the unscoped,
cross-tenant `registry:admin`, which authorizes any `AppId`) via
`AppIdScopeEvaluator.CanAdminister`. This is still true even though both
"Auth + Orchestration" and "Multi-Tenancy" (`08-build-plan.md`) have
since landed — the coarse `registry:admin`-shaped scope check happens as
an ASP.NET Core authorization policy ahead of the handler, and the
fine-grained per-`AppId` check happens inside it, but neither step ever
derives `AppId` itself from the token; the caller must still assert it.
`ChangeKind` (`ADR-016`) and
`EntityIdField` (`ADR-021`) are both required registration fields with no
default — `UpcastFromPrevious`/`DowncastToPrevious` (`ADR-018`/`ADR-028`)
are optional, meaningful only for version `>= 2`, and evaluated via a
pluggable `IUpcastExpressionEvaluator` (CEL by default, `ADR-053`) since
`ADR-037` removed OData `compute()` expressions from this design
entirely. Out of scope: the merge semantics `ChangeKind` actually drives
at fold time (`ADR-016`, `docs/features/cqrs-projections.md`), and
`RequiredClaims` registration/enforcement — a separate field on the same
entity, covered by [`auth.md`](auth.md) and
[`event-security.md`](event-security.md), not re-derived here.

## Sequence diagram

```plantuml
@startuml SchemaRegistry_Sequence
autonumber
actor "Platform Operator" as operator
participant "Registry\n(RegistrationEndpoint)" as endpoint
participant "Auth\n(JWT Bearer + scope policy)" as auth
participant "SchemaRegistryService" as registry
participant "IJsonPathTranslator\n(impl per provider)" as jsonPath
participant "IUpcastExpressionEvaluator\n(CEL by default, ADR-053)" as evaluator
database "Event & Schema Store" as db

operator -> endpoint: PUT /registry/{event-type}\nAuthorization: Bearer <JWT>\n{ appId, jsonSchema, filterableFields, changeKind,\n  entityIdField, entityType?, parentValidationMode?,\n  upcastFromPrevious?, downcastToPrevious? }
endpoint -> auth: validate token + hold SOME registry:admin-shaped scope\n(coarse ASP.NET Core policy, ahead of the handler)
alt missing/invalid token
  auth --> operator: 401
else valid token, no registry:admin-shaped scope at all
  auth --> operator: 403
else coarse scope present
  auth --> endpoint: ok (caller's own scope claim; AppId itself is never\nderived from it -- the caller must still assert it in the body)
  endpoint -> endpoint: AppIdScopeEvaluator.CanAdminister(user, request.AppId) --\nscopes.Contains("registry:admin") OR\nscopes.Contains($"registry:admin:{request.AppId}") (ADR-030's\nfine-grained half, checked against the body's OWN appId)
  alt caller's scope doesn't cover this request's AppId
    endpoint --> operator: 403
  else authorized for this AppId
    endpoint -> registry: register(request.AppId, eventType, jsonSchema, filterableFields,\nchangeKind, entityIdField, parentValidationMode,\nupcastFromPrevious, downcastToPrevious)
    registry -> registry: validate jsonSchema is well-formed JSON Schema
    registry -> registry: validate each filterableFields[].jsonPath resolves in jsonSchema
    registry -> registry: validate changeKind is present and is Full or Partial (ADR-016 -- no default, 400 if missing/invalid)
    registry -> registry: validate entityIdField is present (ADR-021 -- no default, 400 if missing)
    registry -> registry: validate parentValidationMode in {Strict, Permissive}
    alt upcastFromPrevious or downcastToPrevious supplied (version >= 2 only)
      registry -> evaluator: parse expression list, validate every alias\nnames a real property of this version's schema
      evaluator --> registry: ok, or parse/alias error
    end
    alt any validation fails
      registry --> operator: 400
    else all valid
      registry -> registry: determine version = active version for (AppId, Name) + 1 (or 1 if new)
      registry -> db: BEGIN TRANSACTION
      registry -> db: INSERT EventTypeDefinition (AppId, Name, Version, ...), FilterableField rows
      loop for each filterableFields[i] where IsIndexed = true
        registry -> jsonPath: apply provider-specific index/computed-column migration
        jsonPath -> db: CREATE INDEX / ALTER TABLE ... ADD computed column + index
      end
      registry -> db: mark new (AppId, Name) version IsActive = true, prior version IsActive = false
      registry -> db: COMMIT
      registry -> registry: invalidate OpenAPI cache and this AppId's GraphQL SDL cache (ADR-002, ADR-037)
      registry --> operator: 201
    end
  end
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml SchemaRegistry_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * AppId : string <<PK>>
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  JsonSchema : text
  RegisteredAt : datetimeoffset
  IsActive : bool
  ParentValidationMode : enum {Strict, Permissive}
  ChangeKind : enum {Full, Partial}
  EntityIdField : string
  UpcastFromPrevious : string?
  DowncastToPrevious : string?
}

entity "FilterableField" as ff {
  * Id : int <<PK>>
  --
  EventTypeAppId : string <<FK>>
  EventTypeName : string <<FK>>
  EventTypeVersion : int <<FK>>
  JsonPath : string
  DataType : enum {String, Number, Boolean, DateTimeOffset}
  IsIndexed : bool
}

etd ||--o{ ff : "(AppId, Name, Version) = (EventTypeAppId, EventTypeName, EventTypeVersion)"

note right of etd
  Registering a new version inserts a new
  (AppId, Name, Version) row (ADR-030) and flips
  IsActive on the prior version for that same
  (AppId, Name) -- old versions are never mutated
  or deleted (see 05-schema-registry-and-spec-
  generation.md, "Versioning policy"). Two
  different AppIds can register "OrderPlaced"
  independently with zero collision.
end note
@enduml
```

This is the one relationship in the whole data model that's a real,
DB-enforced foreign key end to end — both sides are hand-authored at
registration time, unlike `StoredEvent`'s soft references to this table
(see [`publish-event.md`](publish-event.md),
[`follow-subscribe.md`](follow-subscribe.md)). Full column list (`RejectionBehavior`,
`RequiredSignature`, `DeprecatedAt`, and the rest) is in
`../data/schema-registry.md` — this diagram shows only what registration's
own scenarios below actually touch.

## Control-plane actions are reserved events in the same Event Log (ADR-067)

Registering a schema isn't only the `EventTypeDefinition` write shown in
the sequence diagram above — it also publishes a reserved, platform-level
`SchemaRegistered` event into the *same* Event Log every business event
uses, same `StoredEvent` shape, same hash chain (`ADR-019`), carrying the
registering caller's `ActorId` ([`auth.md`](auth.md), `ADR-064`). It's
reserved the same way `ADR-020`'s `EventUpcastFailed` already is: no
operator ever registers `SchemaRegistered` itself via `PUT /registry/
{event-type}`, it's built into the platform. `EventTypeDefinition` is
folded from this reserved event stream, the same relationship
`EntityStoreRow` already has to ordinary business events (`ADR-021`) —
registration's own validation/persistence above is unchanged, this is an
additional, always-fired side effect of a successful registration, not a
replacement for it. Because it's an ordinary `StoredEvent`, it's traceable
through the existing Lineage API and can be linked via `parentEventIds`
(`ADR-005`) wherever a genuine causal relationship to a specific business
event exists — not a blanket requirement that every business event link
back to its own schema registration (`SchemaVersion` already captures
that relationship without lineage).

## PCI-SAD registration boundary (ADR-071)

A schema author declaring the reserved `x-masking.regulatoryClassification`
value `"PCI-SAD"` on any field makes registration — not publish — hard-
reject the event type outright with `400`. This is scoped narrowly to the
specific data PCI-DSS Requirement 3.2/3.2.2 prohibits storing under any
circumstances, including encrypted: CVV2/CVC2/CID, full magnetic-
stripe/track data, and PIN blocks. It's deliberately **not** a rejection
of full PAN (the card number itself) — a full PAN is ordinary `"PCI"`
classification, already fully covered by `ADR-009`'s masking and
`ADR-057`'s crypto-shredding, no different from any other classified
field. `PCI-SAD` exists precisely because those two mechanisms both still
write the real value into `Payload` before protecting it, which is
incompatible with a rule that the value must never be persisted at all —
so the only correct enforcement point is registration, before the event
type is ever active, not publish. Like every other
`regulatoryClassification` value, this relies on honest self-declaration
by the schema author; this framework cannot reliably detect "this field
holds a CVV" by inspection.

## Salt (UI mockup)

Not applicable — no admin UI is in scope for v1; the registry is an API only
(see `../README.md`, "What this system deliberately is not").

## Gherkin

```gherkin
Feature: Schema registry
  As a platform operator
  I want to register event types with their JSON Schema and filterable fields
  So that publishers and followers have a single, versioned source of truth

  # Every request in this file carries a Bearer token scoped to
  # registry:admin:demo unless a scenario says otherwise, AND every
  # request body below names "appId": "demo" explicitly -- AppId is a
  # required field on the request body itself
  # (RegisterEventTypeRequest.AppId), checked against the token's scope
  # by AppIdScopeEvaluator, never derived from that scope (ADR-030). See
  # auth.md for authentication/authorization behavior itself.

  Scenario: Registering a new event type creates version 1
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": [ { "jsonPath": "$.Amount", "dataType": "Number", "isIndexed": true } ],
        "changeKind": "Full",
        "entityIdField": "$.OrderId"
      }
      """
    Then the response status should be 201
    And "demo:OrderPlaced" version 1 should be the active version
    And a database index should exist for "demo:OrderPlaced" field "$.Amount"

  Scenario: Registering the same type name under a different AppId creates an independent version 1
    # ADR-030 -- (AppId, Name, Version) is the real key; two applications
    # never collide even with an identical type name and different shapes.
    Given AppId "demo" has "OrderPlaced" version 1 registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
      """
    When I PUT "/registry/OrderPlaced" with a Bearer token scoped to "registry:admin:acme" and body:
      """
      {
        "appId": "acme",
        "jsonSchema": { "type": "object", "properties": { "TotalCents": { "type": "integer" } }, "required": ["TotalCents"] },
        "filterableFields": [],
        "changeKind": "Partial",
        "entityIdField": "$.OrderRef"
      }
      """
    Then the response status should be 201
    And "acme:OrderPlaced" version 1 should be the active version, independent of "demo:OrderPlaced"

  Scenario: Registering with a body appId the caller's scoped token does not cover is rejected
    # AppIdScopeEvaluator.CanAdminister -- a registry:admin:demo-scoped
    # token never authorizes a request whose OWN body names a different
    # AppId, even though the coarse registry:admin-shaped policy above it
    # already let the request reach the handler.
    When I PUT "/registry/OrderPlaced" with a Bearer token scoped to "registry:admin:demo" and body:
      """
      {
        "appId": "acme",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": [],
        "changeKind": "Full",
        "entityIdField": "$.OrderId"
      }
      """
    Then the response status should be 403

  Scenario: Registering an updated schema creates a new version and deactivates the previous one
    Given "demo:OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
      """
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount"] },
        "filterableFields": [ { "jsonPath": "$.Amount", "dataType": "Number", "isIndexed": true } ],
        "changeKind": "Full",
        "entityIdField": "$.OrderId",
        "upcastFromPrevious": "Amount, 'Unknown' as Status",
        "downcastToPrevious": "Amount"
      }
      """
    Then the response status should be 201
    And "demo:OrderPlaced" version 2 should be the active version
    And "demo:OrderPlaced" version 1 should remain readable but inactive

  Scenario: Registering without a required changeKind or entityIdField is rejected
    # ADR-016/ADR-021 -- unlike parentValidationMode, neither has a safe default.
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": []
      }
      """
    Then the response status should be 400

  Scenario: Registering a filterable field whose path does not exist in the schema is rejected
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": [ { "jsonPath": "$.DoesNotExist", "dataType": "String", "isIndexed": false } ],
        "changeKind": "Full",
        "entityIdField": "$.OrderId"
      }
      """
    Then the response status should be 400

  Scenario: Fetching the currently active schema for an event type
    Given "demo:OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
      """
    When I GET "/registry/OrderPlaced?appId=demo"
    Then the response status should be 200
    And the response body should equal the registered schema for version 1

  Scenario: Registering a schema regenerates the OpenAPI document and this AppId's GraphQL schema
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": [],
        "changeKind": "Full",
        "entityIdField": "$.OrderId"
      }
      """
    Then "/openapi.json" should include a path "/publish/OrderPlaced"
    And AppId "demo"'s GraphQL schema should include a Subscription field "on_demo_OrderPlaced"

  Scenario: Registering a schema publishes a traceable, hash-chained SchemaRegistered reserved event (ADR-067)
    When I PUT "/registry/OrderPlaced" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] },
        "filterableFields": [],
        "changeKind": "Full",
        "entityIdField": "$.OrderId"
      }
      """
    Then the response status should be 201
    And a reserved "SchemaRegistered" event for "demo:OrderPlaced" version 1 should exist in the Event Log
    And that event's ActorId should be the registering caller's verified identity
    And that event should be hash-chained into the same Event Log as ordinary business events
    And the Lineage API should be able to trace that reserved event like any other

  Scenario: Registering an event type declaring a PCI-SAD field is hard-rejected at registration (ADR-071)
    When I PUT "/registry/CardAuthorization" with body:
      """
      {
        "appId": "demo",
        "jsonSchema": {
          "type": "object",
          "properties": {
            "Amount": { "type": "number" },
            "Cvv": { "type": "string", "x-masking": { "regulatoryClassification": "PCI-SAD" } }
          },
          "required": ["Amount", "Cvv"]
        },
        "filterableFields": [],
        "changeKind": "Full",
        "entityIdField": "$.AuthorizationId"
      }
      """
    Then the response status should be 400
    And "demo:CardAuthorization" should never become an active event type
    # Full PAN is unaffected -- an ordinary "PCI"-classified field (not
    # "PCI-SAD") registers and publishes exactly as ADR-009/ADR-057 already
    # describe for any other classified field.

  Scenario: Listing registered event types via the GraphQL registry-listing query (ADR-037)
    Given AppId "demo" has "OrderPlaced", "OrderCancelled", "PaymentReceived" each registered with a minimal schema
    When I QUERY "/graphql" with document:
      """
      query { eventTypes(first: 2) { name version isActive } }
      """
    Then the response should include exactly 2 event types
    When I QUERY "/graphql" with document:
      """
      query { eventTypes(first: 50) { name version isActive } }
      """
    Then the response should include all 3 event types
```
