# Feature: Entities (`EntityId`, the always-on Entity Store, `ExpectedVersion`)

Context: decision record `ADR-021` (`EntityId`, the always-on Entity Store)
in `../07-adrs.md`, refined by `ADR-022` (`Optional<T>` property-level
patches), `ADR-023` (persist-everything ingestion — the `202` + status-
envelope shape every publish now returns), and `ADR-024` (`ExpectedVersion`
optimistic concurrency + `ConflictFlag`). Entity Store row shape is in
[`../data/entity-store.md`](../data/entity-store.md); the `EntityId`,
`ExpectedVersion`, `SchemaStatus`, and `ConflictFlag` columns on the event
itself are in [`../data/event-log.md`](../data/event-log.md).

This doc covers only what's specific to entities and the Entity Store.
It deliberately does **not** re-derive:
- `ChangeKind`'s general `Full`/`Partial` merge mechanics or the
  `Optional<T>` fold rule's three-state table — those are `ADR-016`/
  `ADR-022` and are exercised end-to-end in
  [`cqrs-projections.md`](cqrs-projections.md) against a *custom* read
  model. This doc shows the same `Full`/`Partial` distinction landing on
  the *default*, always-on Entity Store row instead — a different
  projection target, same source rule.
- `parentEventIds`/lineage (`ADR-005`, [`event-chains.md`](event-chains.md))
  — `EntityId` and `parentEventIds` are deliberately different axes
  (`ADR-021`'s Consequences): one says "this event patches entity X," the
  other says "this event is causally derived from events A, B, C." Neither
  subsumes the other, and this doc's scenarios never rely on parents.
- Claims/masking/`AuthorityStatus` gating — those are
  [`event-security.md`](event-security.md), [`masking.md`](masking.md),
  and the queued authority-rejection design; entities fold regardless of
  those advisory statuses, exactly as persist-everything (`ADR-023`)
  intends.
- Sharding, GraphQL transport, or peer-sync divergence resolution — those
  are `ADR-034`, `ADR-037`, `ADR-033`, layered on top of the same
  `Version`/`ConflictFlag` mechanism this doc exercises directly against
  the store, without a GraphQL Gateway or a second replica in the loop.

Every event type must declare an `EntityIdField` (a JSON path into its
`Payload`, e.g. `$.OrderId`) at registration time — see
[`schema-registry.md`](schema-registry.md) and `ADR-021`'s Consequences —
so this doc's Background registers it explicitly rather than assuming a
default; there isn't one.

## Sequence diagram — publish through to the Entity Store fold

Publish itself is unchanged in shape from
[`publish-event.md`](publish-event.md)'s `202` response (`ADR-023`); what's
new here is the asynchronous `Router` → `EventStore.Fold` path that turns a
persisted event into an Entity Store row. All four entity-specific branches
— brand-new entity, existing-entity update, stale `ExpectedVersion`, and a
schema-invalid payload — are shown as alternatives of the same fold step,
since they differ only in what the fold step decides, not in the request
shape.

![Sequence diagram — publish through to the Entity Store fold](../diagrams/features/entity-concept/01-sequence-diagram-publish-through-to-the-entity-sto.svg)

```plantuml
@startuml Entity_Publish_Fold_Sequence
autonumber
actor "Publishing System" as publisher
participant "PublishEndpoint\n(Inbox)" as inbox
participant "Router" as router
participant "SchemaValidationService" as validator
participant "Entity Resolver" as resolver
participant "EventStore.Fold\n(always-on projector, ADR-021)" as fold
database "Event Log" as eventLog
database "Entity Store" as entityStore

publisher -> inbox: POST /publish/OrderShipped\n{ payload: { OrderId: "o-1", Carrier: "UPS" }, expectedVersion: 1 }
inbox -> eventLog: INSERT StoredEvent\n(Status: received, EntityId: null yet, ExpectedVersion: 1)
inbox --> publisher: 202 { correlationId, status: "received",\n  entityId: null, schemaStatus: null }
...picked up by the Router, asynchronously (ADR-023)...
router -> validator: validate(payload, schema)
alt payload violates the registered schema
  validator --> router: SchemaStatus: invalid
  note right: known properties (here, both OrderId and Carrier)\nstill fold normally -- never rejected (ADR-023)
else payload conforms
  validator --> router: SchemaStatus: conformant
end
router -> resolver: resolve EntityId via EntityIdField "$.OrderId"
resolver --> router: EntityId = "demo:Order:o-1"
router -> router: set (in-memory, not yet saved) StoredEvent.EntityId, .SchemaStatus
note right of router
  No persisted "processing" row ever exists (RouterWorker.ProcessEventAsync,
  "Entity-Centric Core Rebuild"). Every mutation below -- EntityId,
  SchemaStatus, the fold itself, and Status = "applied" -- happens against
  the SAME tracked StoredEvent/EntityStoreRow entities in one worker tick
  and commits in a single SaveChangesAsync() call after the whole received
  batch is processed. The row is only ever observed as "received" (before
  this tick) or "applied" (after it) -- never as an intermediate persisted
  state in between.
end note
router -> fold: fold(StoredEvent)
fold -> entityStore: SELECT EntityStoreRow WHERE EntityId = "demo:Order:o-1"
alt no existing row (origin event for this entity)
  fold -> entityStore: track new EntityStoreRow\n(Version: 1, Data: { OrderId: "o-1", Carrier: "UPS" })
else existing row found
  alt ExpectedVersion supplied and stale\n(does not match EntityStoreRow.Version)
    fold -> fold: set (in-memory) StoredEvent.ConflictFlag = true
    note right: the patch still applies -- ExpectedVersion\nnever blocks a fold, only flags it (ADR-024)
  end
  fold -> entityStore: track EntityStoreRow update\nData = <folded per ChangeKind/Optional<T>>, Version = Version + 1
end
router -> router: set (in-memory) StoredEvent.Status = "applied"
router -> eventLog: SaveChangesAsync() -- one commit for this whole tick's\nbatch of received events AND their EntityStoreRow folds together
@enduml
```

## Sequence diagram — querying an entity's change history

`entityHistory(entityId, property)` is a direct read of the Event Log
filtered by `EntityId`, ordered by `SequenceNumber` — no new storage, and
no dependency on the Entity Store row itself (`ADR-024`). It travels over
the GraphQL Gateway (`ADR-037`) like every other read; `follow-
subscribe.md`/`event-chains.md` cover the Gateway's shared scope/claim
checks in depth and aren't repeated here.

![Sequence diagram — querying an entity's change history](../diagrams/features/entity-concept/02-sequence-diagram-querying-an-entity-s-change-histo.svg)

```plantuml
@startuml Entity_History_Query_Sequence
autonumber
actor "Consuming System" as client
participant "GraphQL Handler" as handler
participant "History Resolver\n(ADR-024 §8.4)" as historyResolver
database "Event Log" as eventLog

client -> handler: QUERY { entityHistory(entityId: "demo:Order:o-1") {\n  eventId sequenceNumber occurredAt expectedVersion conflictFlag schemaStatus } }
handler -> historyResolver: resolve(entityId)
historyResolver -> eventLog: SELECT * FROM StoredEvent\nWHERE EntityId = "demo:Order:o-1"\nORDER BY SequenceNumber
alt no event has ever resolved to this EntityId
  eventLog --> historyResolver: empty
  historyResolver --> client: empty list (not a 404 -- an entity is\nonly ever known by having been folded at least once)
else at least one event found
  eventLog --> historyResolver: every event that ever patched this entity,\nin append order, each with its own ConflictFlag/SchemaStatus
  historyResolver --> client: [ { eventId, sequenceNumber, occurredAt,\n    expectedVersion, conflictFlag, schemaStatus }, ... ]
end
@enduml
```

## Data model (ER diagram)

![Data model (ER diagram)](../diagrams/features/entity-concept/03-data-model-er-diagram.svg)

```plantuml
@startuml EntityConcept_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  ChangeKind : string
  EntityIdField : string
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string <<FK>>
  EventType : string
  ExpectedVersion : bigint?
  ConflictFlag : bool
  LateArrivalFlag : bool
  OccurredAt : datetimeoffset
  SchemaStatus : string?
  Status : string
  Payload : text
}

entity "EntityStoreRow" as entityStore {
  * EntityId : string <<PK>>
  --
  EntityType : string
  Version : bigint
  Data : text
  Extensions : text
  SchemaVersion : int
  LastAppliedSequenceNumber : bigint
  LastAppliedLogicalTime : datetimeoffset
}

etd ..> event : "EntityIdField resolves\nEntityId at fold time\n-- logical only, not a DB FK"
event "*" --> "1" entityStore : "folds into, in OccurredAt order,\nnever the other direction (ADR-021)"

note right of entityStore
  One row per EntityId, always present --
  not opt-in like a custom CQRS read model
  (ProjectionSnapshot/OrderSummary live in
  a separate ProjectionsDbContext entirely).
  Version bumps only when Data actually
  changes; ExpectedVersion on the event is
  compared against this column at fold time.
end note

note right of event
  ConflictFlag and LateArrivalFlag answer two
  different questions at fold time (ADR-029;
  see also docs/patterns/interactions/fold-
  ordering-and-conflict.md): ConflictFlag
  compares ExpectedVersion against
  EntityStoreRow.Version (a stale causal
  basis) and never blocks the write.
  LateArrivalFlag compares OccurredAt against
  EntityStoreRow.LastAppliedLogicalTime (a
  chronologically-stale event) and DOES
  withhold that event's change from the
  affected property. A single event can set
  both, independently.
end note
@enduml
```

Full column list for both entities is in
[`../data/event-log.md`](../data/event-log.md) and
[`../data/entity-store.md`](../data/entity-store.md) — this diagram shows
only what the fold step actually reads/writes.

## Salt (UI mockup)

Not applicable — the Entity Store is a fold target and read path with no
UI surface of its own; see `ADR-039`/`mvvm-client-architecture.md` for
where a UI eventually reads current-state entities from (the GraphQL
Gateway's `Entity Resolver`), out of scope for this doc.

## Gherkin

```gherkin
Feature: Entities (EntityId, the always-on Entity Store, ExpectedVersion)
  As the event store
  I want every published event to fold into exactly one versioned Entity Store row
  So that "current state of X" is always answerable without replaying raw events, and concurrent patches are flagged rather than silently lost

  # Every request in this file carries a Bearer token with the events:publish
  # scope unless a scenario says otherwise. See auth.md for authentication/
  # authorization behavior itself. EntityId format is {appId}:{entityType}:
  # {uniqueId} (ADR-021); scenarios below use appId "demo" throughout.

  Background:
    Given the event type "OrderPlaced" version 1 is registered with ChangeKind "Full" and EntityIdField "$.OrderId" and schema:
      """
      {
        "type": "object",
        "properties": { "OrderId": { "type": "string" }, "CustomerName": { "type": "string" }, "Amount": { "type": "number" } },
        "required": ["OrderId", "Amount"]
      }
      """
    And the event type "OrderShipped" version 1 is registered with ChangeKind "Partial" and EntityIdField "$.OrderId" and schema:
      """
      {
        "type": "object",
        "properties": { "OrderId": { "type": "string" }, "Carrier": { "type": "string" } },
        "required": ["OrderId", "Carrier"]
      }
      """

  Scenario: Publishing an event that resolves to a brand-new EntityId creates an Entity Store row
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 } }
      """
    Then the response status should be 202 with status "received"
    And eventually the stored event's status should become "applied" with EntityId "demo:Order:o-1"
    And an EntityStoreRow for "demo:Order:o-1" should exist with Version 1
    And its Data should equal { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }

  Scenario: Publishing a second event for the same EntityId updates the row and increments Version
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    And its fold has completed, leaving EntityStoreRow "demo:Order:o-1" at Version 1
    When an "OrderShipped" event is published with body { "OrderId": "o-1", "Carrier": "UPS" }
    Then eventually the EntityStoreRow for "demo:Order:o-1" should be at Version 2
    And its Data should still have CustomerName "A. Smith" and Amount 42.00

  Scenario: A Full event's payload replaces the Entity Store row's Data wholesale
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    When another "OrderPlaced" event (ChangeKind Full) is published with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 99.00 }
    Then eventually the EntityStoreRow for "demo:Order:o-1" Data should equal exactly { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 99.00 }
    # A Full payload is not Optional<T>-wrapped (ADR-022) -- every property present
    # replaces the prior snapshot outright, unlike the Partial merge below.

  Scenario: A Partial event's unknown property is folded into the Entity Store row's Extensions bag, not dropped
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    When an "OrderShipped" event is published with body { "OrderId": "o-1", "Carrier": "UPS", "TrackingNumber": "1Z999" }
    Then eventually the EntityStoreRow for "demo:Order:o-1" Data should have Carrier "UPS"
    And its Extensions should contain { "TrackingNumber": "1Z999" }
    # TrackingNumber isn't in OrderShipped's registered schema -- ADR-022's
    # Extensions routing applies it anyway, matching ADR-023's persist-everything
    # posture rather than discarding the unrecognized field.

  Scenario: Publishing with a stale ExpectedVersion sets ConflictFlag but still persists and folds
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    And an "OrderShipped" event was published and folded for "o-1", advancing EntityStoreRow "demo:Order:o-1" to Version 2
    When an "OrderShipped" event is published with body { "OrderId": "o-1", "Carrier": "FedEx" } and expectedVersion 1
    Then the response status should still be 202
    And eventually the stored event's ConflictFlag should be true
    And the EntityStoreRow for "demo:Order:o-1" should still advance to Version 3 with Carrier "FedEx"
    # ExpectedVersion never blocks or rejects a fold -- it only flags the later
    # event as conflicting (ADR-024). The earlier event is never retroactively touched.

  Scenario: An event with an older OccurredAt arriving after a logically newer one already folded sets LateArrivalFlag and does not overwrite
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    And an "OrderShipped" event "e-B" was published and folded for "o-1" with OccurredAt "2026-08-01T12:00:00Z" and body { "OrderId": "o-1", "Carrier": "UPS" }
    And its fold advanced EntityStoreRow "demo:Order:o-1"'s LastAppliedLogicalTime to "2026-08-01T12:00:00Z"
    When an "OrderShipped" event "e-A" is published for "o-1" with OccurredAt "2026-08-01T11:00:00Z" (earlier than the high-water mark) and body { "OrderId": "o-1", "Carrier": "FedEx" }
    Then the response status should still be 202
    And eventually "e-A"'s stored event should have LateArrivalFlag true
    And "e-A"'s ConflictFlag should be false
    And the EntityStoreRow for "demo:Order:o-1" Data should still have Carrier "UPS", never "FedEx"
    And the EntityStoreRow's LastAppliedSequenceNumber should have advanced past "e-A", but its Version should not have incremented
    # LateArrivalFlag (ADR-029) is a distinct question from ConflictFlag (ADR-024):
    # "e-A" carries no stale ExpectedVersion at all here -- it's flagged purely
    # because its OccurredAt is behind EntityStoreRow.LastAppliedLogicalTime, i.e.
    # it logically happened before "e-B", which already won the Carrier property.
    # Unlike ConflictFlag, LateArrivalFlag actually withholds the change -- applying
    # it would silently revert already-folded newer state (see
    # docs/patterns/interactions/fold-ordering-and-conflict.md).

  Scenario: An event that is both a stale-ExpectedVersion conflict and a late arrival sets both flags independently
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    And an "OrderShipped" event was published and folded for "o-1" with OccurredAt "2026-08-01T12:00:00Z" and body { "OrderId": "o-1", "Carrier": "UPS" }, advancing EntityStoreRow "demo:Order:o-1" to Version 2 and LastAppliedLogicalTime to "2026-08-01T12:00:00Z"
    When an "OrderShipped" event is published with body { "OrderId": "o-1", "Carrier": "FedEx" }, expectedVersion 1 (stale -- actual Version is 2), and OccurredAt "2026-08-01T11:00:00Z" (earlier than the high-water mark)
    Then the response status should still be 202
    And eventually the stored event's ConflictFlag should be true
    And the stored event's LateArrivalFlag should be true
    And the EntityStoreRow for "demo:Order:o-1" Data should still have Carrier "UPS", not "FedEx"
    And the EntityStoreRow's Version should remain 2
    # The "Fail/Fail" row of docs/patterns/interactions/fold-ordering-and-conflict.md's
    # table: ConflictFlag's stale-ExpectedVersion check and LateArrivalFlag's
    # OccurredAt-vs-high-water-mark check run independently in the same fold step
    # and can both fire on one event without either being aware of the other.
    # Because LateArrivalFlag gates the write, Version stays at 2 here -- if only
    # ConflictFlag had fired (this doc's earlier scenario), the write would have
    # gone through and Version would have advanced to 3 instead.

  Scenario: Publishing without ExpectedVersion applies unconditionally, with no conflict detection
    Given an "OrderPlaced" event was published for "o-1" with body { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }
    When an "OrderShipped" event is published with body { "OrderId": "o-1", "Carrier": "UPS" } and no expectedVersion supplied
    Then eventually the stored event's ConflictFlag should be false
    And the EntityStoreRow for "demo:Order:o-1" should advance to Version 2
    # ExpectedVersion is opt-in (ADR-021/ADR-024) -- omitting it matches this
    # entity's fold behavior before ADR-024 existed at all: no check, no flag.

  Scenario: A schema-invalid publish persists with 202 and SchemaStatus invalid, and known properties still fold
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": "not-a-number" } }
      """
    Then the response status should be 202, not 400
    And eventually the stored event's SchemaStatus should be "invalid"
    And the stored event's Status should still become "applied"
    And an EntityStoreRow for "demo:Order:o-1" should exist with OrderId and CustomerName folded normally
    # Amount fails schema validation, but OrderId/CustomerName are still known,
    # recognized properties -- ADR-023 folds what it can rather than rejecting
    # the whole payload over one bad field.

  Scenario: Querying an entity's change history returns every contributing event in order
    Given an "OrderPlaced" event "e-1" was published and folded for "o-1"
    And an "OrderShipped" event "e-2" was published and folded for "o-1" with expectedVersion 1, and no conflict
    And a second "OrderShipped" event "e-3" was published and folded for "o-1" with a stale expectedVersion, setting ConflictFlag
    When I query entityHistory(entityId: "demo:Order:o-1")
    Then the response should list "e-1", "e-2", "e-3" in that order
    And "e-3" alone should show conflictFlag true
```
