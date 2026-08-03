# Feature: Event-type security (required claims)

Context: data model fields in `../02-data-model.md` ("Event-type security");
API contract in `../03-api-contracts.md` ("Event-type security (required
claims)" and "`RequiredClaims` and the Lineage API"); decision record
`ADR-008` in `../07-adrs.md`, generalized from one fixed claim per
direction to an `OR`-matched list by `ADR-050`; implementation shape (why
this isn't a static policy) in `../06-solution-structure.md`. Builds on
[`publish-event.md`](publish-event.md), [`follow-subscribe.md`](follow-subscribe.md),
[`event-chains.md`](event-chains.md), and [`auth.md`](auth.md) — this doc
covers only what's specific to `RequiredClaims` (the `{Direction, Claim}`
list gating publish/read access per event type).

This is a second, independent authorization dimension from the scopes in
`auth.md`: scopes gate the *operation*; this feature gates the *event
type*. Both must pass. Follow and Lineage below are shown using their
actual GraphQL Subscription/Query syntax, carried over the HTTP `QUERY`
method (`ADR-012`/`ADR-037`) — not `GET` — since a `where`/`eventId`
argument can carry PII; the transport and surface change doesn't affect
any of the claim-checking logic this doc is about.

## Sequence diagram — publish gated by RequiredClaims (Publish direction)

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
  else a Publish-direction RequiredClaims entry is configured and caller's token holds none of them
    endpoint --> publisher: 403 (missing required claim for event type)
  else no Publish-direction entry, or caller holds at least one (OR semantics, ADR-050)
    endpoint -> endpoint: proceed to schema validation, parent-link\nvalidation, append (see publish-event.md) --\npersists with 202 + advisory SchemaStatus regardless of shape (ADR-023)
  end
end
@enduml
```

## Sequence diagram — follow gated by RequiredClaims (Read direction)

```plantuml
@startuml EventSecurity_Follow_Sequence
autonumber
actor "Consuming System" as follower
participant "GraphQL Gateway\n(HotChocolate, graphql-sse over SSE)" as gateway
participant "Auth\n(events:follow scope)" as scopeAuth
database "Event & Schema Store" as db

follower -> gateway: QUERY /graphql\nBearer <JWT>\nbody: subscription { onOrderPlaced { ... } }
gateway -> scopeAuth: validate events:follow scope
alt missing scope
  scopeAuth --> follower: connection rejected 403
else scope present
  gateway -> db: SELECT EventTypeDefinition WHERE Name = event-type AND IsActive
  alt event-type unknown (no matching subscription field in this AppId's schema)
    gateway --> follower: GraphQL validation error, rejected before any\nresolver runs -- see follow-subscribe.md (ADR-037)
  else a Read-direction RequiredClaims entry is configured and caller's token holds none of them
    gateway --> follower: connection rejected 403 (missing required claim for event type)
  else no Read-direction entry, or caller holds at least one (OR semantics, ADR-050)
    gateway -> gateway: proceed to where-argument validation, SSE stream\n(see follow-subscribe.md) -- checked once, not per event
  end
end
@enduml
```

## Sequence diagram — lineage: per-node visibility ("you can only see what you can see")

```plantuml
@startuml EventSecurity_Lineage_Sequence
autonumber
actor "Consuming System" as client
participant "GraphQL Gateway\n(Lineage query fields)" as gateway
participant "Auth\n(events:lineage:read scope)" as scopeAuth
database "Event & Schema Store" as db

client -> gateway: QUERY /graphql\nBearer <JWT>\nbody: query { event(eventId: "...") { ancestors(first: 50) { ... } } }
gateway -> scopeAuth: validate events:lineage:read scope
alt missing scope
  scopeAuth --> client: 403
else scope present
  gateway -> db: does the named eventId exist?
  alt unknown eventId
    gateway --> client: 404
  else known eventId, but caller lacks a Read-direction RequiredClaims\nentry for ITS OWN type
    gateway --> client: 403 for the whole request\n(can't query the lineage of something you can't see at all)
  else root visible
    gateway -> db: resolve full node set (root + transitive closure, per event-chains.md;\nrecursion stops at any node whose type is restricted -- see 06-solution-structure.md)
    gateway -> gateway: independently, per DISCOVERED node (not the root, already handled above):\ncheck Read-direction RequiredClaims against caller's claims
    gateway -> gateway: a node the caller can't see -> { eventId, resolved: true, restricted: true } stub, leaf, no recursion past it.\nEvery other node -- reachable via a different path, or unrelated -- returns normally, regardless.
    gateway --> client: 200 [ mix of full nodes and restricted:true stubs, per event-chains.md ]
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
}

entity "RequiredClaim" as rc {
  * EventTypeName : string <<FK>>
  * EventTypeVersion : int <<FK>>
  --
  Direction : enum {Publish, Read}
  Claim : string <<"type:value">>
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventType : string
  SchemaVersion : int
}

etd ||--o{ rc : "(Name, Version) = (EventTypeName, EventTypeVersion)"
etd ..> event : "logical only -- EventType/SchemaVersion,\nNOT a DB foreign key (same as elsewhere)"

note right of rc
  RequiredClaims generalizes RequiredPublishClaim/RequiredReadClaim
  from one fixed claim per direction to a list (ADR-050). Multiple
  entries for the same Direction are OR'ed by default -- holding
  ANY ONE of them satisfies the gate; ADR-008's original "exactly
  one claim per direction" limitation no longer applies.
end note
@enduml
```

Full entity set is in `../02-data-model.md`. Nothing new is added to
`StoredEvent` or `EventParents` for this feature — the `RequiredClaims`
list lives entirely on `EventTypeDefinition`, checked against whichever
`StoredEvent` rows a request touches.

## Salt (UI mockup)

Not applicable — this is enforcement logic with no UI surface.

## Gherkin

```gherkin
Feature: Event-type security (required claims)
  As the event store
  I want a per-event-type list of required claims, OR'ed within each direction
  So that sensitive event types can restrict who may write or see their data,
  independently of the general events:publish/events:follow/events:lineage:read scopes

  Background:
    Given client "publisher-client" has scope "events:publish"
    And client "follower-client" has scopes "events:follow" and "events:lineage:read"
    And the event type "PatientAdmitted" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "PatientId": { "type": "string" } }, "required": ["PatientId"] }
      """
    And "PatientAdmitted" requires RequiredClaims: [{Direction: Publish, Claim: "clearance:phi"}, {Direction: Read, Claim: "clearance:phi"}]
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
    Then the response status should be 202
    # 202, not 201 -- ADR-023's persist-everything posture; the claim check
    # itself is unaffected and still runs before storage, unlike a
    # schema/version problem, which now always persists (see publish-event.md).

  Scenario: Holding any one of multiple required claims for the same direction satisfies the gate (OR semantics, ADR-050)
    Given the event type "LabResultRecorded" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Value": { "type": "number" } }, "required": ["Value"] }
      """
    And "LabResultRecorded" requires RequiredClaims: [{Direction: Publish, Claim: "role:lab-tech"}, {Direction: Publish, Claim: "role:lab-supervisor"}]
    And I have a Bearer token for client "publisher-client" with claim "role" value "lab-supervisor"
    When I POST to "/publish/LabResultRecorded" with body:
      """
      { "payload": { "Value": 5.2 } }
      """
    Then the response status should be 202
    # Holding only the second of two OR'ed Publish-direction claims still
    # satisfies the gate -- ADR-008's original "exactly one claim per
    # direction" limitation no longer applies (ADR-050).

  Scenario: Publish and read claims are independent
    Given "OrderShipped" version 1 is registered requiring RequiredClaims: [{Direction: Publish, Claim: "role:warehouse"}]
    And I have a Bearer token for client "follower-client" with no additional claims
    When I open a GraphQL Subscription connection with document:
      """
      subscription { onOrderShipped { eventId } }
      """
    Then the connection should be accepted
    When I POST to "/publish/OrderShipped" with body:
      """
      { "payload": {} }
      """
    Then the response status should be 403

  Scenario: Connecting to follow a claim-gated event type without the required claim is rejected
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I open a GraphQL Subscription connection with document:
      """
      subscription { onPatientAdmitted { patientId } }
      """
    Then the connection should be rejected with 403

  Scenario: Connecting to follow a claim-gated event type with the required claim succeeds
    Given I have a Bearer token for client "follower-client" with claim "clearance" value "phi"
    When I open a GraphQL Subscription connection with document:
      """
      subscription { onPatientAdmitted { patientId } }
      """
    Then the connection should be accepted

  Scenario: An unclaimed event type is unaffected
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I open a GraphQL Subscription connection with document:
      """
      subscription { onOrderPlaced { amount } }
      """
    Then the connection should be accepted

  Scenario: A lineage query on a restricted root is rejected entirely
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I query:
      """
      query { event(eventId: "admit-1") { ancestors(first: 50) { eventId eventType resolved restricted } } }
      """
    Then the response status should be 403

  Scenario: A lineage query on a visible root still succeeds when a discovered node is restricted, stubbed not failed
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I query:
      """
      query { event(eventId: "order-1") { descendants(first: 50) { eventId eventType resolved restricted } } }
      """
    Then the response status should be 200
    And the response should include "admit-1" as { "resolved": true, "restricted": true } with no eventType, sequenceNumber, or occurredAt

  Scenario: A lineage query succeeds and shows full detail once the caller holds every required claim
    Given an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 }
    And a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" } parented off "order-1"
    And I have a Bearer token for client "follower-client" with claim "clearance" value "phi"
    When I query:
      """
      query { event(eventId: "order-1") { descendants(first: 50) { eventId eventType resolved restricted } } }
      """
    Then the response status should be 200
    And the response should include "admit-1" fully, not stubbed

  Scenario: Lacking access to a parent does not hide an otherwise-visible child
    Given a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" }
    And an "OrderPlaced" event "order-1" was published with body { "Amount": 150.00 } parented off "admit-1"
    And I have a Bearer token for client "follower-client" with no additional claims
    When I query:
      """
      query { event(eventId: "order-1") { ancestors(first: 50) { eventId eventType resolved restricted } } }
      """
    Then the response status should be 200
    And the response should include "admit-1" as { "resolved": true, "restricted": true }
    When I query:
      """
      query { event(eventId: "order-1") { parents { eventId eventType resolved restricted } } }
      """
    Then the response status should be 200
    # order-1 itself is fully visible even though its own parent is restricted --
    # visibility of a node never depends on the visibility of its neighbors.
    # parents has no first argument, unlike ancestors/descendants -- it has
    # no recursion depth to bound (03-api-contracts.md).

  Scenario: A restricted-but-existing event is distinguishable from an unknown one (403 vs 404)
    Given I have a Bearer token for client "follower-client" with no additional claims
    When I query:
      """
      query { event(eventId: "00000000-0000-0000-0000-000000000000") { parents { eventId } } }
      """
    Then the response status should be 404
    Given a "PatientAdmitted" event "admit-1" was published with body { "PatientId": "abc-123" }
    When I query:
      """
      query { event(eventId: "admit-1") { parents { eventId } } }
      """
    Then the response status should be 403
    # 403, not 404 -- ADR-008 deliberately leaks that admit-1 exists rather
    # than hiding it behind a uniform 404 for both cases.
```
