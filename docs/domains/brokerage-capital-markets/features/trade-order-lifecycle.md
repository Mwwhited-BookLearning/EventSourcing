# Feature: Trade Order Lifecycle and Recordkeeping

Context: this doc exercises three ADRs together against one worked
example — a trade order moving `Submitted` → `RiskChecked` →
`Executed` → `Settled`. `ADR-019`
([`../../../adrs/adr-019-hash-chained-tamper-evidence.md`](../../../adrs/adr-019-hash-chained-tamper-evidence.md))
gives the store-wide `ChainHash` every one of this order's events
participates in, already confirmed (`ADR-071`) to satisfy SEC Rule
17a-4's broker-dealer recordkeeping audit-trail alternative with no new
mechanism. `ADR-071`
([`../../../adrs/adr-071-pci-sad-registration-boundary.md`](../../../adrs/adr-071-pci-sad-registration-boundary.md))
is exercised via a card-funded deposit schema registration that either
hard-rejects (a declared `PCI-SAD` field) or succeeds (an ordinary
masked PAN field only). `ADR-067`
([`../../../adrs/adr-067-control-plane-actions-as-reserved-events.md`](../../../adrs/adr-067-control-plane-actions-as-reserved-events.md))
supplies the reserved `RoleGranted`/`SchemaRegistered` control-plane
events, both landing in the *same* Event Log and hash chain as the
order's own business events. `ADR-005`
([`../../../adrs/adr-005-event-parenting-dag.md`](../../../adrs/adr-005-event-parenting-dag.md))'s
`parentEventIds` mechanism is what causally links each lifecycle step
to the one before it, and — per `ADR-067`'s own named example — links
a business event to the specific control-plane grant that authorized
the actor performing it. The full `StoredEvent` envelope shape is in
[`../../../data/event-log.md`](../../../data/event-log.md); the
`x-masking`/`regulatoryClassification` schema shape (including the
reserved `"PCI-SAD"` value) is in
[`../../../data/schema-registry.md`](../../../data/schema-registry.md).
This domain's own applicability of every ADR cited here is recorded in
[`../README.md`](../README.md)'s "Applicable ADRs" section — nothing
below invents a mechanism that section doesn't already list.

This doc deliberately does **not** re-derive:
- Ordinary property-level masking or `ADR-057` crypto-shredding
  mechanics for a full PAN (card number) field — that's `ADR-009`/
  `ADR-057`, already covered in
  [`../../../data/schema-registry.md`](../../../data/schema-registry.md)'s
  "Event-type security" section. A full PAN is **not** Sensitive
  Authentication Data and is unaffected by `ADR-071` — this doc only
  exercises the narrower SAD boundary itself.
- Row-level security / claims-check mechanics for traders, compliance,
  and back-office roles (`ADR-046`/`ADR-043`) — this doc's compliance
  officer scenario shows a role being *granted*, not the read-time
  claim check that role later satisfies.
- `ADR-045`'s separate `AccessLog` entity shape — mentioned only to
  contrast why control-plane *writes* (this doc) land in the same
  Event Log while access *reads* deliberately don't (`ADR-067`'s own
  stated reasoning, restated briefly below, not re-derived in depth).
- The general publish/schema-registration request/response mechanics
  already shown in `../../../features/publish-event.md` and
  `../../../features/schema-registry.md` — this doc only adds what's
  specific to a trade order's own event types and the `PCI-SAD`
  registration boundary.

Everywhere below uses the illustrative `AppId` `"brokerage"`, so
`EntityId`s look like `brokerage:Order:ord-1` (`ADR-021`'s
`{appId}:{entityType}:{uniqueId}` format).

## Sequence diagram — order lifecycle, causally linked and hash-chained end to end

Two mechanisms are deliberately in play at once here, and they answer
different questions (see `ADR-019`'s and `ADR-005`'s own
disambiguation): `ChainHash` is a single, store-wide sequential chain —
*every* `StoredEvent` ever appended extends it, business or
control-plane, regardless of `EntityId` — while `parentEventIds` is a
per-event, causal DAG scoped to whatever a specific event actually
derives from. This diagram shows both: a compliance officer's role
grant (`ADR-067`, reserved event) lands earlier in the *same* global
chain the order's own events extend, and the risk-check step names
*both* the order it evaluates and that role grant as parents — two
different parents, two different reasons, per `ADR-005`.

```plantuml
@startuml TradeOrderLifecycle_Sequence
autonumber
actor "Compliance Admin" as admin
actor "Order Management System" as oms
actor "Risk Officer" as reviewer
actor "Clearing System" as clearing
participant "RBAC Admin API\n(ADR-046/067)" as rbacApi
participant "Publish API\n(Inbox, ADR-023)" as endpoint
participant "EventAppender\n(ChainHash, ADR-019)" as appender
database "Event Log" as eventLog

admin -> rbacApi: POST /admin/roles/compliance-officer/grant\n{ granteeActorId: "reviewer-1" }
rbacApi -> appender: append(RoleGranted -- reserved control-plane\nevent type, ADR-067) ActorId: "admin-1"
appender -> eventLog: INSERT StoredEvent\n(SequenceNumber: 118, EventType: "RoleGranted",\n ChainHash: SHA-256(chain[117] || hash(payload) || 118))
appender --> rbacApi: ok
rbacApi --> admin: 201 { eventId: "rg-1" }
note over eventLog
  Role/UserPermission (ADR-046) fold from this event the
  same way EntityStoreRow folds a business event (ADR-067) --
  same StoredEvent shape, same hash chain, ActorId always set.
end note

oms -> endpoint: POST /publish/OrderSubmitted\n{ payload: { OrderId: "ord-1", AccountId: "acct-42",\n  Symbol: "ACME", Side: "Buy", Quantity: 500, LimitPrice: 41.10 } }
endpoint -> appender: append(StoredEvent, no parents -- origin event, ADR-005)
appender -> eventLog: INSERT StoredEvent\n(SequenceNumber: 119, EntityId: "brokerage:Order:ord-1",\n ActorId: "oms-system", ChainHash: SHA-256(chain[118] || hash || 119))
endpoint --> oms: 202 { correlationId, status: "received" }

reviewer -> endpoint: POST /publish/OrderRiskChecked\n{ payload: { OrderId: "ord-1", Decision: "Approved", Reviewer: "reviewer-1" },\n  parentEventIds: ["119", "rg-1"] }
note right: two parents, two different reasons (ADR-005/067) --\n"119" is the OrderSubmitted event this risk check evaluates;\n"rg-1" is the RoleGranted event that authorized reviewer-1\nto perform risk checks at all
endpoint -> appender: append(StoredEvent, EventParents x2)
appender -> eventLog: INSERT StoredEvent (SequenceNumber: 120, ActorId: "reviewer-1",\n ChainHash: SHA-256(chain[119] || hash || 120));\nINSERT EventParents (120,119), (120,"rg-1")
endpoint --> reviewer: 202 { correlationId, status: "received" }

alt Decision = "Approved"
  oms -> endpoint: POST /publish/OrderExecuted\n{ payload: { OrderId: "ord-1", ExecutionPrice: 41.05, ExecutedQuantity: 500 },\n  parentEventIds: ["120"] }
  endpoint -> appender: append(StoredEvent, EventParents x1)
  appender -> eventLog: INSERT StoredEvent (SequenceNumber: 121,\n ChainHash: SHA-256(chain[120] || hash || 121)); INSERT EventParents (121,120)
  endpoint --> oms: 202

  clearing -> endpoint: POST /publish/OrderSettled\n{ payload: { OrderId: "ord-1", SettledAt: "2026-08-03T00:00:00Z",\n  SettlementAmount: 20525.00 }, parentEventIds: ["121"] }
  endpoint -> appender: append(StoredEvent, EventParents x1)
  appender -> eventLog: INSERT StoredEvent (SequenceNumber: 122,\n ChainHash: SHA-256(chain[121] || hash || 122)); INSERT EventParents (122,121)
  endpoint --> clearing: 202
  note over eventLog
    The full T+1 lifecycle is now four causally-linked events
    (119 -> 120 -> 121 -> 122, ADR-005) that are ALSO four
    consecutive links in the one store-wide ChainHash sequence
    (ADR-019) -- replaying SequenceNumber 1..122 and comparing
    the final ChainHash detects any later tampering with any of them.
  end note
else Decision = "Rejected"
  oms -> endpoint: POST /publish/OrderRejected\n{ payload: { OrderId: "ord-1", Reason: "Exceeds account buying power" },\n  parentEventIds: ["120"] }
  endpoint -> appender: append(StoredEvent, EventParents x1)
  appender -> eventLog: INSERT StoredEvent (SequenceNumber: 121,\n ChainHash: SHA-256(chain[120] || hash || 121)); INSERT EventParents (121,120)
  endpoint --> oms: 202
  note over eventLog
    No OrderExecuted/OrderSettled is ever published for ord-1 --
    the lifecycle terminates structurally at OrderRejected, not
    via a status flag on OrderSubmitted.
  end note
end
@enduml
```

## Sequence diagram — schema registration and the PCI-SAD boundary

Registering `CardDepositAuthorized` (a card-funded account deposit)
is where `ADR-071`'s boundary bites: a schema author who declares a
`Cvv2` field with `x-masking.regulatoryClassification: "PCI-SAD"` gets
a hard `400` at registration — before any `StoredEvent` for that type
could ever exist — while the same event type registered *without* a
SAD field succeeds normally and, per `ADR-067`, also appends a
reserved `SchemaRegistered` control-plane event into the same Event
Log and hash chain the order lifecycle above extends.

```plantuml
@startuml TradeOrderLifecycle_SchemaRegistration_Sequence
autonumber
actor "Schema Author" as author
participant "Registry\n(RegistrationEndpoint)" as endpoint
participant "SchemaRegistryService" as registry
participant "EventAppender\n(ChainHash, ADR-019)" as appender
database "Event & Schema Store" as db

author -> endpoint: PUT /registry/CardDepositAuthorized\nAuthorization: Bearer <JWT registry:admin>\n{ jsonSchema: { properties: { PAN: {...}, Cvv2: { type: "string",\n  "x-masking": { "regulatoryClassification": "PCI-SAD" } } } } }
endpoint -> registry: register(eventType, jsonSchema, ...)
registry -> registry: scan every property's x-masking.regulatoryClassification (ADR-071)
alt any property declares regulatoryClassification == "PCI-SAD"
  registry --> endpoint: 400 (Sensitive Authentication Data may never be\nregistered as a schema field, encrypted or not -- PCI-DSS Req 3.2/3.2.2)
  endpoint --> author: 400
  note right
    Registration hard-rejects the WHOLE event type outright --
    the one place this design still enforces reject-on-invalid
    after ADR-023's persist-everything posture (ADR-071). No
    StoredEvent, no SchemaRegistered event -- nothing is appended.
  end note
else no PCI-SAD field declared\n(author instead registers PAN alone, regulatoryClassification "PCI",\nno CVV/track-data/PIN-block field at all)
  registry -> db: BEGIN TRANSACTION
  registry -> db: INSERT EventTypeDefinition, FilterableField rows\n(CardDepositAuthorized v1)
  registry -> db: mark version IsActive = true
  registry -> appender: append(SchemaRegistered -- reserved\ncontrol-plane event, ADR-067) ActorId: "schema-author-1"
  appender -> db: INSERT StoredEvent\n(SequenceNumber: 205, EventType: "SchemaRegistered",\n ChainHash: SHA-256(chain[204] || hash(payload) || 205))
  registry -> db: COMMIT
  registry -> registry: invalidate OpenAPI/AsyncAPI cache (ADR-002)
  registry --> endpoint: 201
  endpoint --> author: 201 { eventTypeName: "CardDepositAuthorized", version: 1 }
  note right
    Same StoredEvent shape, same Event Log, same ChainHash
    sequence every ordinary business event above already
    extends (ADR-067) -- EventTypeDefinition is itself now a
    read model folded from this event, not hand-authored state.
  end note
end
@enduml
```

Full PAN remains fully covered by ordinary masking (`ADR-009`) and
crypto-shredding (`ADR-057`) — not re-derived here (see Context).
`ADR-045`'s separate `AccessLog` is the opposite storage choice from
`SchemaRegistered` above deliberately, not inconsistently: reads
vastly outnumber writes and don't causally cause anything, so they get
their own store, while this control-plane write is structurally
identical to any other event and benefits from participating in the
same lineage DAG (`ADR-067`'s own stated reasoning).

## Data model (ER diagram)

```plantuml
@startuml TradeOrderLifecycle_ER
hide circle
skinparam linetype ortho

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string <<FK>>
  EventType : string
  Payload : text
  PayloadHash : string
  ChainHash : string
  ActorId : string
  OccurredAt : datetimeoffset
}

entity "EventParent" as parent {
  * ChildEventId : uuid <<PK, FK>>
  * ParentEventId : uuid <<PK>>
}

entity "EntityStoreRow" as entityStore {
  * EntityId : string <<PK>>
  --
  Version : bigint
  Data : text
  LastAppliedSequenceNumber : bigint
}

entity "EventTypeDefinition" as etd {
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  JsonSchema : text
  EntityIdField : string
}

event ||--o{ parent : "ChildEventId -- real FK,\nADR-005"
event ..o{ parent : "ParentEventId -- no DB FK,\nmust tolerate Permissive dangling refs"
event "*" --> "1" entityStore : "folds into, in OccurredAt\norder (ADR-021)"
etd ..> event : "EntityIdField resolves EntityId;\nx-masking.regulatoryClassification\nchecked at registration only (ADR-071)"

note right of event
  ChainHash chains off the immediately preceding
  SequenceNumber's ChainHash -- a single store-wide
  sequence, not per-EntityId (ADR-019). A control-plane
  SchemaRegistered/RoleGranted event (ADR-067) is a
  StoredEvent like any other and extends the exact same
  chain the OrderSubmitted..OrderSettled events do.
end note

note right of etd
  A "Cvv2" property here declaring
  x-masking.regulatoryClassification: "PCI-SAD"
  is what makes registration itself reject the
  whole event type (400), before any StoredEvent
  for it could ever exist (ADR-071).
end note
@enduml
```

Full column lists are in
[`../../../data/event-log.md`](../../../data/event-log.md) (`StoredEvent`,
`EventParent`) and
[`../../../data/schema-registry.md`](../../../data/schema-registry.md)
(`EntityStoreRow`, `EventTypeDefinition`) — this diagram, and the
sketch below, show only the fields this doc's scenarios actually
touch:

```csharp
// Subset only -- canonical definitions in ../../../data/event-log.md
// and ../../../data/schema-registry.md; not redefined here.
public class StoredEvent
{
    public long SequenceNumber { get; set; }             // global arrival order -- the ONE sequence ChainHash chains across, not per-EntityId (ADR-019)
    public string EntityId { get; set; } = default!;      // "brokerage:Order:ord-1" -- {appId}:{entityType}:{uniqueId} (ADR-021)
    public string EventType { get; set; } = default!;      // "OrderSubmitted" | "OrderRiskChecked" | "OrderExecuted" | "OrderRejected" | "OrderSettled" | "RoleGranted" | "SchemaRegistered" (ADR-067's reserved control-plane types)
    public string PayloadHash { get; set; } = default!;     // ADR-011
    public string ChainHash { get; set; } = default!;       // SHA-256(prior ChainHash || PayloadHash || SequenceNumber) (ADR-019)
    public string ActorId { get; set; } = default!;          // verified caller identity, ALWAYS populated (ADR-064) -- "reviewer-1", "schema-author-1", "admin-1", "oms-system", ...
}

public class EventParent
{
    public Guid ChildEventId { get; set; }    // e.g. OrderExecuted's EventId
    public Guid ParentEventId { get; set; }   // e.g. OrderRiskChecked's EventId, or a RoleGranted control-plane event's EventId (ADR-067)
}
```

## Salt (UI mockup)

A compliance/back-office order-history screen: the lifecycle timeline
with a chain-integrity badge, plus a control-plane audit panel showing
the reserved events from both sequence diagrams above landing in the
same log.

```plantuml
@startsalt
{
  { "Order  ord-1  (Trade Order Lifecycle)" }
  ..
  | Step | Event | Actor | SequenceNumber |
  | 1 | OrderSubmitted | oms-system | 119 |
  | 2 | OrderRiskChecked (Approved) | reviewer-1 | 120 |
  | 3 | OrderExecuted | oms-system | 121 |
  | 4 | OrderSettled | clearing-system | 122 |
  ..
  { "Chain integrity verified through SequenceNumber 122" | [X] } | [ Re-verify now ]
  ..
  { "Control-plane audit (ADR-067)" }
  | Event | Actor | SequenceNumber |
  | RoleGranted (compliance-officer -> reviewer-1) | admin-1 | 118 |
  | SchemaRegistered (CardDepositAuthorized v1) | schema-author-1 | 205 |
  ..
  [ View full lineage graph ] | [ Export for litigation review ]
}
@endsalt
```

The chain-integrity badge is a display of `GET /events/verify` (`ADR-019`)
re-run through this order's own `SequenceNumber` range, not a
per-entity mechanism of its own. "Export for litigation review" reuses
`ADR-068`'s lineage export/manifest mechanism unchanged — not built out
further in this doc (see Context).

## Gherkin

```gherkin
Feature: Trade Order Lifecycle and Recordkeeping
  As a broker-dealer's compliance/back-office function
  I want a trade order's Submitted/RiskChecked/Executed/Settled steps causally
    linked and hash-chained end to end, and PCI-SAD payment data kept out of
    the log entirely
  So that SEC Rule 17a-4 recordkeeping is satisfiable from the Event Log alone,
    and a card-funded deposit's authentication secrets can never be persisted

  # Every request below carries a Bearer token with sufficient scope
  # (events:publish for order events, registry:admin for schema registration,
  # a narrow RBAC-admin scope for role grants) unless a scenario says
  # otherwise. See ../../../features/auth.md for authentication/authorization
  # behavior itself -- not re-derived here.

  Background:
    Given the event type "OrderSubmitted" version 1 is registered with ChangeKind "Full" and EntityIdField "$.OrderId" and schema:
      """
      {
        "type": "object",
        "properties": { "OrderId": { "type": "string" }, "AccountId": { "type": "string" }, "Symbol": { "type": "string" }, "Side": { "type": "string" }, "Quantity": { "type": "number" }, "LimitPrice": { "type": "number" } },
        "required": ["OrderId", "AccountId", "Symbol", "Side", "Quantity"]
      }
      """
    And the event type "OrderRiskChecked" version 1 is registered with ChangeKind "Partial" and EntityIdField "$.OrderId" and schema:
      """
      {
        "type": "object",
        "properties": { "OrderId": { "type": "string" }, "Decision": { "type": "string" }, "Reviewer": { "type": "string" } },
        "required": ["OrderId", "Decision"]
      }
      """
    And the event type "OrderExecuted" version 1 is registered with ChangeKind "Partial" and EntityIdField "$.OrderId" and schema:
      """
      { "type": "object", "properties": { "OrderId": { "type": "string" }, "ExecutionPrice": { "type": "number" }, "ExecutedQuantity": { "type": "number" } }, "required": ["OrderId"] }
      """
    And the event type "OrderRejected" version 1 is registered with ChangeKind "Partial" and EntityIdField "$.OrderId" and schema:
      """
      { "type": "object", "properties": { "OrderId": { "type": "string" }, "Reason": { "type": "string" } }, "required": ["OrderId", "Reason"] }
      """
    And the event type "OrderSettled" version 1 is registered with ChangeKind "Partial" and EntityIdField "$.OrderId" and schema:
      """
      { "type": "object", "properties": { "OrderId": { "type": "string" }, "SettledAt": { "type": "string" }, "SettlementAmount": { "type": "number" } }, "required": ["OrderId"] }
      """
    And the role "compliance-officer" is granted to actor "reviewer-1", recorded as a "RoleGranted" event "rg-1"
      # RoleGranted is a reserved, platform-level event type (ADR-067) -- no
      # operator registers it via PUT /registry/{event-type}; it exists the
      # same way EventUpcastFailed (ADR-020) already does.

  Scenario: A trade order's full lifecycle is causally linked and lands in the one hash chain
    When I POST to "/publish/OrderSubmitted" for AppId "brokerage" with body:
      """
      { "payload": { "OrderId": "ord-1", "AccountId": "acct-42", "Symbol": "ACME", "Side": "Buy", "Quantity": 500, "LimitPrice": 41.10 } }
      """
    Then the response status should be 202 with EntityId "brokerage:Order:ord-1"
    When actor "reviewer-1" POSTs to "/publish/OrderRiskChecked" with body:
      """
      { "payload": { "OrderId": "ord-1", "Decision": "Approved", "Reviewer": "reviewer-1" }, "parentEventIds": ["<OrderSubmitted eventId>", "rg-1"] }
      """
    Then the response status should be 202
    And the stored event's parents should be exactly ["<OrderSubmitted eventId>", "rg-1"]
    # Two parents, two different reasons (ADR-005): the order this risk check
    # evaluates, and the RoleGranted event that authorized reviewer-1 to
    # perform the check at all -- ADR-067's own named example of linking a
    # business event to the control-plane grant that authorized it.
    When I POST to "/publish/OrderExecuted" with body:
      """
      { "payload": { "OrderId": "ord-1", "ExecutionPrice": 41.05, "ExecutedQuantity": 500 }, "parentEventIds": ["<OrderRiskChecked eventId>"] }
      """
    And I POST to "/publish/OrderSettled" with body:
      """
      { "payload": { "OrderId": "ord-1", "SettledAt": "2026-08-03T00:00:00Z", "SettlementAmount": 20525.00 }, "parentEventIds": ["<OrderExecuted eventId>"] }
      """
    Then all four events (OrderSubmitted, OrderRiskChecked, OrderExecuted, OrderSettled) should be ancestors/descendants of each other per the Lineage API
    And GET "/events/verify?throughSequenceNumber=<OrderSettled's SequenceNumber>" should report no divergence
    # ChainHash is a single, store-wide sequence (ADR-019) -- the same
    # verification call already covers every event in the store, business or
    # control-plane, not just this one order. This is the mechanism ADR-071
    # confirms already satisfies SEC Rule 17a-4's audit-trail alternative.

  Scenario: A risk-check rejection stops the lifecycle before execution, structurally
    Given an "OrderSubmitted" event was published for "ord-2" with body { "OrderId": "ord-2", "AccountId": "acct-42", "Symbol": "ACME", "Side": "Buy", "Quantity": 50000, "LimitPrice": 41.10 }
    When actor "reviewer-1" POSTs to "/publish/OrderRiskChecked" with body:
      """
      { "payload": { "OrderId": "ord-2", "Decision": "Rejected", "Reviewer": "reviewer-1" }, "parentEventIds": ["<OrderSubmitted eventId>", "rg-1"] }
      """
    And I POST to "/publish/OrderRejected" with body:
      """
      { "payload": { "OrderId": "ord-2", "Reason": "Exceeds account buying power" }, "parentEventIds": ["<OrderRiskChecked eventId>"] }
      """
    Then the response status should be 202
    And no "OrderExecuted" or "OrderSettled" event should ever exist for "ord-2"
    # ADR-023's persist-everything posture means OrderRejected is never
    # blocked or retried -- it terminates the lifecycle by simply being the
    # last event ord-2 ever receives, not by flipping a status flag.

  Scenario: Registering an event type with a field declared PCI-SAD is hard-rejected at registration
    When I PUT "/registry/CardDepositAuthorized" with body:
      """
      {
        "jsonSchema": {
          "type": "object",
          "properties": {
            "PAN": { "type": "string", "x-masking": { "requiredClaim": "clearance:pci", "strategy": "PartialReveal", "regulatoryClassification": "PCI" } },
            "Cvv2": { "type": "string", "x-masking": { "regulatoryClassification": "PCI-SAD" } }
          },
          "required": ["PAN", "Cvv2"]
        }
      }
      """
    Then the response status should be 400
    And "CardDepositAuthorized" should not exist as a registered event type at any version
    And no "SchemaRegistered" event should be appended to the Event Log
    # PCI-DSS Requirement 3.2/3.2.2 bars persisting SAD after authorization
    # even encrypted -- ADR-009's masking and ADR-057's crypto-shredding both
    # still write the real value to Payload first, which this rule already
    # prohibits regardless of what happens afterward (ADR-071). This is the
    # one place after ADR-023 that registration still rejects outright.

  Scenario: Registering the same event type without a PCI-SAD field succeeds and emits a reserved control-plane event
    When I PUT "/registry/CardDepositAuthorized" with body:
      """
      {
        "jsonSchema": {
          "type": "object",
          "properties": {
            "PAN": { "type": "string", "x-masking": { "requiredClaim": "clearance:pci", "strategy": "PartialReveal", "regulatoryClassification": "PCI" } }
          },
          "required": ["PAN"]
        },
        "filterableFields": []
      }
      """
    Then the response status should be 201
    And "CardDepositAuthorized" version 1 should be the active version
    And a "SchemaRegistered" event should be appended to the Event Log with ActorId equal to the registering caller's identity
    And that event's ChainHash should extend the same chain sequence the trade order's own events extend
    # Full PAN is not SAD -- ordinary masking/crypto-shredding (ADR-009/057)
    # covers it exactly like any other classified field. SchemaRegistered
    # lands in the SAME Event Log as OrderSubmitted et al. (ADR-067),
    # deliberately unlike ADR-045's separate AccessLog for reads.

  Scenario: A role grant is itself a reserved event, foldable and linkable like any other
    When actor "admin-1" POSTs to "/admin/roles/compliance-officer/grant" with body:
      """
      { "granteeActorId": "reviewer-2" }
      """
    Then the response status should be 201
    And a "RoleGranted" event should be appended to the Event Log with ActorId "admin-1"
    And a "Role" read model row for "compliance-officer" should list "reviewer-2" among its grantees
    # Role/UserPermission fold from this event the same way EntityStoreRow
    # folds a business event (ADR-067) -- no separate RBAC-state table
    # exists outside what this event produces.
```
