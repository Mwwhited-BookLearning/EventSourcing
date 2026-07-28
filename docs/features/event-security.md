# Feature: Event-type security (required claims)

Context: data model fields in `../02-data-model.md` ("Event-type security");
API contract in `../03-api-contracts.md` ("Event-type security (required
claims)" and "RequiredReadClaim and the Lineage API"); decision record
`ADR-008` in `../07-adrs.md`; implementation shape (why this isn't a
static policy) in `../06-solution-structure.md`. Builds on
[`publish-event.md`](publish-event.md), [`follow-subscribe.md`](follow-subscribe.md),
[`event-chains.md`](event-chains.md), and [`auth.md`](auth.md) — this doc
covers only what's specific to `RequiredPublishClaim`/`RequiredReadClaim`.

This is a second, independent authorization dimension from the scopes in
`auth.md`: scopes gate the *operation*; this feature gates the *event
type*. Both must pass. Follow and Lineage below are shown as `GET` for
readability — per `ADR-012` both are actually `QUERY`, with their
parameters in the request body; the method change doesn't affect any of
the claim-checking logic this doc is about.

## Sequence diagram — publish gated by RequiredPublishClaim

```plantuml
@startuml EventSecurity_Publish_Sequence
autonumber
actor "Publishing System" as publisher
participant "Publish API" as endpoint
participant "Auth\n(events:publish scope)" as scopeAuth
participant "SchemaRegistryClient" as registryClient
database "Event & Schema Store" as db

publisher -> endpoint: POST /publish/{event-type}\nBearer <JWT>
endpoint -> scopeAuth: validate events:publish scope
alt missing scope
  scopeAuth --> publisher: 403
else scope present
  endpoint -> registryClient: get active EventTypeDefinition
  registryClient -> db: SELECT ... WHERE Name = event-type AND IsActive
  alt event-type unknown
    registryClient --> publisher: 404
  else RequiredPublishClaim is set and caller's token lacks it
    endpoint --> publisher: 403 (missing required claim for event type)
  else RequiredPublishClaim is null, or caller has it
    endpoint -> endpoint: proceed to schema validation, parent-link\nvalidation, append (see publish-event.md)
  end
end
@enduml
```

## Sequence diagram — follow gated by RequiredReadClaim

```plantuml
@startuml EventSecurity_Follow_Sequence
autonumber
actor "Consuming System" as follower
participant "Follow API" as endpoint
participant "Auth\n(events:follow scope)" as scopeAuth
database "Event & Schema Store" as db

follower -> endpoint: GET /follow/{event-type}?$filter=...\nBearer <JWT>
endpoint -> scopeAuth: validate events:follow scope
alt missing scope
  scopeAuth --> follower: connection rejected 403
else scope present
  endpoint -> db: SELECT EventTypeDefinition WHERE Name = event-type AND IsActive
  alt event-type unknown
    endpoint --> follower: connection rejected 404
  else RequiredReadClaim is set and caller's token lacks it
    endpoint --> follower: connection rejected 403 (missing required claim for event type)
  else RequiredReadClaim is null, or caller has it
    endpoint -> endpoint: proceed to $filter validation, SSE stream\n(see follow-subscribe.md) -- checked once, not per event
  end
end
@enduml
```

## Sequence diagram — lineage: per-node visibility ("you can only see what you can see")

```plantuml
@startuml EventSecurity_Lineage_Sequence
autonumber
actor "Consuming System" as client
participant "Lineage API" as endpoint
participant "Auth\n(events:lineage:read scope)" as scopeAuth
database "Event & Schema Store" as db

client -> endpoint: GET /events/{id}/ancestors\nBearer <JWT>
endpoint -> scopeAuth: validate events:lineage:read scope
alt missing scope
  scopeAuth --> client: 403
else scope present
  endpoint -> db: does {id} exist?
  alt unknown eventId
    endpoint --> client: 404
  else known eventId, but caller lacks RequiredReadClaim for ITS OWN type
    endpoint --> client: 403 for the whole request\n(can't query the lineage of something you can't see at all)
  else root visible
    endpoint -> db: resolve full node set (root + transitive closure, per event-chains.md;\nrecursion stops at any node whose type is restricted -- see 06-solution-structure.md)
    endpoint -> endpoint: independently, per DISCOVERED node (not the root, already handled above):\ncheck RequiredReadClaim against caller's claims
    endpoint -> endpoint: a node the caller can't see -> {eventId, resolved:true, restricted:true} stub, leaf, no recursion past it.\nEvery other node -- reachable via a different path, or unrelated -- returns normally, regardless.
    endpoint --> client: 200 [ mix of full nodes and restricted:true stubs, per event-chains.md ]
  end
end
@enduml
```

Only the root's own visibility is pass/fail (`403` if it exists but is
restricted). Every node the traversal *discovers* is independent —
lacking access to one ancestor never hides a sibling ancestor, a
descendant, or anything else the caller has rights to.

## Data model (ER diagram)

```plantuml
@startuml EventSecurity_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  IsActive : bool
  RequiredPublishClaim : string? <<"type:value">>
  RequiredReadClaim : string? <<"type:value">>
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventType : string
  SchemaVersion : int
}

etd ..> event : "logical only -- EventType/SchemaVersion,\nNOT a DB foreign key (same as elsewhere)"

note right of etd
  Both claim fields are single "type:value"
  strings, or null for no extra restriction.
  Checked via ClaimsPrincipal.HasClaim(type, value)
  -- a single discrete claim, unlike the
  space-delimited "scope" claim (see auth.md).
end note
@enduml
```

Full entity set is in `../02-data-model.md`. Nothing new is added to
`StoredEvent` or `EventParents` for this feature — the two claim fields
live entirely on `EventTypeDefinition`, checked against whichever
`StoredEvent` rows a request touches.

## Salt (UI mockup)

Not applicable — this is enforcement logic with no UI surface.

## Gherkin

```gherkin
Feature: Event-type security (required claims)
  As the event store
  I want a per-event-type required claim, separate for publish and read
  So that sensitive event types can restrict who may write or see their data,
  independently of the general events:publish/events:follow/events:lineage:read scopes

  Background:
    Given client "publisher-client" has scope "events:publish"
    And client "follower-client" has scopes "events:follow" and "events:lineage:read"
    And the event type "PatientAdmitted" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "PatientId": { "type": "string" } }, "required": ["PatientId"] }
      """
    And "PatientAdmitted" requires publish claim "clearance:phi" and read claim "clearance:phi"
    And the event type "OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
      """

  Scenario: Publishing a claim-gated event type without the required claim is rejected
    Given I have a Bearer token for client "publisher-client" with no additional claims
    When I POST to "/publish/PatientAdmitted" with body:
      """
      { "payload": { "PatientId": "abc-123" } }
      """
    Then the response status should be 403
    And the response should state a required claim for the event type is missing

  Scenario: Publishing a claim-gated event type with the required claim succeeds
    Given I have a Bearer token for client "publisher-client" with claim "clearance" value "phi"
    When I POST to "/publish/PatientAdmitted" with body:
      """
      { "payload": { "PatientId": "abc-123" } }
      """
    Then the response status should be 201

  Scenario: Publish and read claims are independent
    Given "OrderShipped" version 1 is registered requiring publish claim "role:warehouse" and no read claim
    And I have a Bearer token for client "follower-client" with no additional claims
    When I open an SSE connection to "/follow/OrderShipped"
    Then the connection should be accepted
    When I POST to "/publish/OrderShipped" with body:
      """
      { "payload": {} }
      """
    Then the response status should be 403

  Scenario: Connecting to follow a claim-gated event type without the required claim is rejected
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I open an SSE connection to "/follow/PatientAdmitted"
    Then the connection should be rejected with 403

  Scenario: Connecting to follow a claim-gated event type with the required claim succeeds
    Given I have a Bearer token for client "follower-client" with claim "clearance" value "phi"
    When I open an SSE connection to "/follow/PatientAdmitted"
    Then the connection should be accepted

  Scenario: An unclaimed event type is unaffected
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I open an SSE connection to "/follow/OrderPlaced"
    Then the connection should be accepted

  Scenario: A lineage query on a restricted root is rejected entirely
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I GET "/events/admit-1/ancestors"
    Then the response status should be 403

  Scenario: A lineage query on a visible root still succeeds when a discovered node is restricted, stubbed not failed
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I GET "/events/order-1/descendants"
    Then the response status should be 200
    And the response should include "admit-1" as { "resolved": true, "restricted": true } with no eventType, sequenceNumber, or occurredAt

  Scenario: A lineage query succeeds and shows full detail once the caller holds every required claim
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with claim "clearance" value "phi"
    When I GET "/events/order-1/descendants"
    Then the response status should be 200
    And the response should include "admit-1" fully, not stubbed

  Scenario: Lacking access to a parent does not hide an otherwise-visible child
    Given a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" }
    And an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 } parented off "admit-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I GET "/events/order-1/ancestors"
    Then the response status should be 200
    And the response should include "admit-1" as { "resolved": true, "restricted": true }
    When I GET "/events/order-1/parents"
    Then the response status should be 200
    # order-1 itself is fully visible even though its own parent is restricted --
    # visibility of a node never depends on the visibility of its neighbors.

  Scenario: A restricted-but-existing event is distinguishable from an unknown one (403 vs 404)
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I GET "/events/00000000-0000-0000-0000-000000000000/parents"
    Then the response status should be 404
    Given a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" }
    When I GET "/events/admit-1/parents"
    Then the response status should be 403
    # 403, not 404 -- ADR-008 deliberately leaks that admit-1 exists rather
    # than hiding it behind a uniform 404 for both cases.
```
