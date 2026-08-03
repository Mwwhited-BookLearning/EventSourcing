# Feature: Multi-origin replication and application-level sharding

Context: this doc deliberately covers two ADRs together, not one —
[`docs/comparisons/README.md`](../comparisons/README.md) already groups
their two decision comparisons side by side as "distribution" concerns,
and neither has any feature doc yet. `ADR-033`
(`../adrs/adr-033-multi-origin-replication.md`) decides the gossip/full-mesh
peer-sync topology, the minimum 2-replica regional-fault-tolerance
requirement, `OriginId`/`LogicalClock` (HLC) ordering, the fault/abend/
restart-tolerant Peer Sync Outbox/Inbox, and Merkle-tree catch-up after a
disconnection — see
[`docs/comparisons/peer-sync-topology.md`](../comparisons/peer-sync-topology.md)
for why gossip won over hub-and-spoke and leaderless pull. `ADR-034`
(`../adrs/adr-034-application-level-sharding.md`) decides `ShardKey =
EntityType` for the Entity Store and requires a fan-out/merge coordinator
for any query spanning shards — see
[`docs/comparisons/sharding-strategy.md`](../comparisons/sharding-strategy.md)
for why entity-type-based won over hash-based consistent hashing. Both
ADRs' fields — `ShardKey`, `LastAppliedOriginId` — live on
`EntityStoreRow` in [`../data/entity-store.md`](../data/entity-store.md).
Cross-shard fan-out is a GraphQL Gateway concern (`ADR-037`), not a new
query surface. Cross-server conflict resolution reuses `ADR-024`'s
`ConflictFlag` outright — no second mechanism (`ADR-033`'s consequences).
See `patterns/README.md`'s "Sharding," "Multi-origin replication +
anti-entropy/gossip," and "Merkle tree catch-up" rows for the general
patterns this doc applies concretely.

## Sequence diagram — peer-sync gossip exchange, then Merkle-tree catch-up after a disconnection

```plantuml
@startuml ReplicationAndSharding_PeerSync_Sequence
autonumber
participant "Site A\nPeer Sync Outbox" as aOutbox
participant "Site A\nPeer Sync Service" as aSync
participant "Site B\nPeer Sync Service" as bSync
participant "Site B\nPeer Sync Inbox" as bInbox
database "Site B\nEvent Log + Entity Store" as bStore

== Normal gossip exchange (both sites reachable) ==
aSync -> aOutbox: read events since PeerSyncCursor["Site B"].LastAckedSequenceNumber
aOutbox --> aSync: batch [ { EventId, OriginId="Site A", LogicalClock, Payload }, ... ]
aSync -> bSync: push batch
bSync -> bInbox: append batch (durable table, not memory -- ADR-033)
bInbox -> bStore: append each event to the local Event Log,\nexactly as if it arrived via Site B's own client Inbox\n(no special-casing, ADR-033)
bStore -> bStore: fold as usual (ADR-024/ADR-029) --\nmay set ConflictFlag/LateArrivalFlag
bSync --> aSync: ack up to SequenceNumber N
aSync -> aOutbox: advance PeerSyncCursor["Site B"].LastAckedSequenceNumber = N

== Disconnection ==
aSync -x bSync: push fails -- Site B unreachable
note over aSync, bSync
  Site A keeps appending to its own durable Peer Sync
  Outbox for Site B; nothing queued is lost, sync just
  falls behind (fault/abend/restart-tolerant, ADR-033).
end note

== Reconnection: Merkle-tree catch-up, not a full resync ==
aSync -> bSync: reconnect; exchange Merkle-tree summary\n(hash per event range, per peer)
bSync --> aSync: its own Merkle-tree summary
aSync -> aSync: diff the two trees -- identify only the\nranges whose hashes disagree
aSync -> bSync: push only the differing ranges\n(not the whole backlog since disconnection)
bSync -> bInbox: append + fold as above
bSync --> aSync: ack up to SequenceNumber N'
@enduml
```

`ADR-033` is explicit that sync performs no routing, schema validation, or
projection of its own — a synced event lands in the receiving site's event
log exactly as if it arrived from its own client Inbox, and that site's own
local router/projector handles it with whatever schema/registry knowledge
it currently has. The Merkle-tree exchange is standard Dynamo/Cassandra-
style anti-entropy — a different application of the hashing discipline
`ADR-019`'s `ChainHash` already established (ranges of the log for catch-up
efficiency here, not tamper evidence there).

## Sequence diagram — a query spanning entity types on different shards

```plantuml
@startuml ReplicationAndSharding_ShardFanout_Sequence
autonumber
actor "Consuming System" as client
participant "GraphQL Gateway" as gateway
participant "Shard Resolver\n(EntityType -> ShardKey, ADR-034)" as resolver
database "Entity Store\nshard: orders" as ordersShard
database "Entity Store\nshard: customers" as customersShard

client -> gateway: QUERY { order(id: "order-1") { ... }\n  customer(id: "customer-1") { ... } }
gateway -> resolver: plan(query) -- which shard serves each field?
resolver -> resolver: ShardKey(Order) = "orders"\nShardKey(Customer) = "customers" (ADR-034)
par query the orders shard
  resolver -> ordersShard: query Order "order-1"
  ordersShard --> resolver: Order entity
else query the customers shard
  resolver -> customersShard: query Customer "customer-1"
  customersShard --> resolver: Customer entity
end
resolver -> resolver: merge both results into one response tree
resolver --> gateway: merged result
gateway --> client: 200 { order: {...}, customer: {...} }
@enduml
```

This is `ADR-034`'s stated consequence made concrete: a GraphQL resolver
spanning entity types that live on different shards issues one query per
shard and merges results, rather than assuming a single-shard query plan
always suffices. A single-shard query (both requested entities on the same
`ShardKey`) skips the `par` branch entirely — one call, no merge step.

## Data residency — region pinning (ADR-061)

`ADR-061` layers one filtering rule onto the peer-sync outbox the first
sequence diagram above already shows, rather than adding a new mechanism.
Every configured peer gains a `Region` tag (e.g. `"eu-west"`, `"us-
east"`), and a new per-`AppId` `AllowedRegions` list (`docs/data/schema-
registry.md`) constrains which regions that `AppId`'s events may
replicate to. Enforcement happens at the outbox, not at fold/query time:
when an event belonging to a region-constrained `AppId` is queued for
outbound gossip sync, the outbox filters candidate destination peers down
to only those tagged with one of the `AppId`'s `AllowedRegions` — a
disallowed-region peer simply never receives that event in a sync batch.
`ADR-034`'s `ShardKey = EntityType` is completely unchanged by this;
region constrains *where a shard's replicas may live*, not a new
sharding dimension layered onto the key itself.

```plantuml
@startuml ReplicationAndSharding_RegionPinning_Sequence
autonumber
participant "Site A (eu-west)\nPeer Sync Outbox" as aOutbox
participant "Site A\nPeer Sync Service" as aSync
participant "Site B (eu-west)" as bSite
participant "Site C (us-east)" as cSite

note over aOutbox
  AppId "eu-tenant" has AllowedRegions: ["eu-west"] (ADR-061).
  Site A, Site B, and Site C are all configured gossip peers,
  tagged eu-west/eu-west/us-east respectively.
end note
aSync -> aOutbox: read events queued for sync, for AppId "eu-tenant"
aOutbox -> aOutbox: filter candidate peers to those tagged\nwith an AllowedRegions region ("eu-west")
aSync -> bSite: push batch (Site B is tagged "eu-west" -- allowed)
note over aSync, cSite
  Site C is tagged "us-east", not in AllowedRegions --
  "eu-tenant"'s events are never included in any batch
  bound for Site C. This is a filtering rule at the
  existing outbox, not a rejected/failed sync attempt.
end note
@enduml
```

This is a **named, honest tension with `ADR-033`'s minimum-replication-
factor-of-2 requirement**, not silently resolved: a tenant restricted to
a single region satisfies both requirements only if that region has ≥2
live sites tagged with it. Where it doesn't, residency wins — it's a
regulatory hard constraint, fault tolerance is a design preference — and
the deployment is responsible for ensuring ≥2 sites exist in any region a
tenant restricts to, or knowingly accepting single-site risk.

## Data model (ER diagram)

```plantuml
@startuml ReplicationAndSharding_ER
hide circle
skinparam linetype ortho

entity "EntityStoreRow" as entityStore {
  * EntityId : string <<PK>>
  --
  EntityType : string
  ShardKey : string
  Version : bigint
  LastAppliedSequenceNumber : bigint
  LastAppliedOriginId : string <<nullable>>
  LateArrivalFlag : bool
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string <<FK>>
  OriginId : string
  LogicalClock : string
  ConflictFlag : bool
}

entity "PeerSyncCursor" as cursor {
  * PeerId : string <<PK>>
  --
  LastReceivedSequenceNumber : bigint
  LastAckedSequenceNumber : bigint
  LastSyncAttemptAt : datetimeoffset
  LastSyncSuccessAt : datetimeoffset
}

event }o--|| entityStore : "EntityId -- real FK,\nfolded into current state"
entityStore ..> cursor : "LastAppliedOriginId = PeerId --\nlogical only, no DB FK\n(OriginId travels per-event, ADR-033)"
event ..> cursor : "OriginId = PeerId --\nsame logical link, on the\nsource-of-truth event row"

note right of entityStore
  ShardKey computed from EntityType
  (ADR-034) -- every entity of a given
  type lands on the same shard,
  independently of which site wrote it.
end note

note right of cursor
  Durable per-peer resumption point --
  survives an unclean process
  termination (ADR-033). Merkle-tree
  catch-up starts its range comparison
  from here, not from SequenceNumber 0.
end note
@enduml
```

There is deliberately no `Peer`/`Site` table with a hard foreign key from
`EntityStoreRow.LastAppliedOriginId` or `StoredEvent.OriginId` — both are
logical-only links to whichever peer identifier a `PeerSyncCursor` row
uses, the same "logical only, not a DB FK" discipline `follow-
subscribe.md`'s ER diagram already uses for `EventTypeDefinition ..>
StoredEvent`. `ShardKey` and `LastAppliedOriginId` are the two fields
`ADR-034`/`ADR-033` respectively add to `EntityStoreRow`
(`../data/entity-store.md`); `OriginId`/`LogicalClock`/`ConflictFlag` are
carried on `StoredEvent` (`../data/event-log.md`) — `ConflictFlag` is
`ADR-024`'s pre-existing field, reused here for cross-origin conflicts,
not a new one.

## Salt (UI mockup)

Not applicable — replication and sharding are server-to-server and
resolver-internal mechanisms with no UI surface in scope.

## Gherkin

```gherkin
Feature: Multi-origin replication and application-level sharding
  As an operator running this event store across more than one site
  I want events to replicate between sites and entities to shard predictably by type
  So that any single site can go dark without losing data or availability,
     and a query spanning entity types still returns one merged result

  # Every site in this file authenticates its peer-sync traffic the same way
  # any other server-to-server caller does; see auth.md. This doc only
  # covers replication/sharding mechanics, not that authentication itself.

  Background:
    Given the event type "OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }
      """
    And the event type "CustomerRegistered" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "CustomerId": { "type": "string" }, "Name": { "type": "string" } }, "required": ["CustomerId", "Name"] }
      """
    And entity type "Order" is shard-mapped to ShardKey "orders" (ADR-034)
    And entity type "Customer" is shard-mapped to ShardKey "customers" (ADR-034)
    And "Site A" and "Site B" are configured as gossip peers, each replicating every shard (ADR-033)
    And the "orders" shard's minimum replication factor of 2 is satisfied by "Site A" and "Site B"

  Scenario: An event published at one site eventually replicates to its peer
    When an "OrderPlaced" event for "order-1" is published at "Site A"
    Then eventually "Site A"'s Entity Store should show an "Order" entity "order-1"
    And eventually "Site B"'s Entity Store should also show that same "Order" entity "order-1"
    And "Site B"'s copy should have LastAppliedOriginId "Site A"

  Scenario: Two sites disconnect, write independently, and reconnect via Merkle-tree diff rather than a full resync
    Given "Site A" and "Site B" are fully in sync
    When connectivity between "Site A" and "Site B" is lost
    And an "OrderPlaced" event for "order-2" is published at "Site A" while disconnected
    And an "OrderPlaced" event for "order-3" is published at "Site B" while disconnected
    And connectivity between "Site A" and "Site B" is restored
    Then the two sites should exchange Merkle-tree summaries of their event ranges before transferring anything
    And only the event ranges that actually differ should be transferred, not the full event log
    And eventually both "Site A" and "Site B" should show both "order-2" and "order-3"

  Scenario: A conflicting concurrent write from two origins is flagged, reusing ConflictFlag rather than a new mechanism
    Given an "Order" entity "order-1" exists at Version 5, already synced to both "Site A" and "Site B"
    When "Site A" publishes a patch to "order-1" with ExpectedVersion 5
    And "Site B" independently publishes a conflicting patch to "order-1" with ExpectedVersion 5, before the sites next sync
    And "Site A" and "Site B" then sync with each other
    Then the patch that loses the fold-time conflict check should have ConflictFlag set to true
    And no origin-specific or second conflict-resolution mechanism should be involved -- it is the exact same ConflictFlag ADR-024 defines for a same-server concurrent write

  Scenario: An entity of a given EntityType always resolves to the same shard
    When "Order" entities "order-10" and "order-11" are both created
    Then both should resolve to ShardKey "orders"
    When a "Customer" entity "customer-1" is created
    Then it should resolve to ShardKey "customers", a different shard than either Order

  Scenario: A query spanning entity types on different shards fans out and merges results
    Given an "Order" entity "order-1" exists on the "orders" shard
    And a "Customer" entity "customer-1" exists on the "customers" shard
    When a single GraphQL query requests both "order-1" and "customer-1" in one request
    Then the GraphQL Gateway should issue one query against the "orders" shard and one against the "customers" shard
    And the response should merge both results into a single response tree, indistinguishable from a single-shard query

  Scenario: Losing one entire site still leaves the system serving reads and writes from the surviving site
    Given the "orders" shard is replicated across "Site A" and "Site B" (minimum factor of 2, ADR-033)
    When "Site A" goes dark entirely (a full regional outage)
    Then "Site B" should continue to accept new "OrderPlaced" publishes
    And "Site B" should continue to serve reads against its own Entity Store
    And no event or entity already synced to "Site B" before the outage should be lost

  Scenario: An AppId with a restricted AllowedRegions list never replicates to a peer tagged with a different region (ADR-061)
    Given "Site A" and "Site B" are both tagged Region "eu-west"
    And "Site C" is tagged Region "us-east" and is also a configured gossip peer of "Site A"
    And AppId "eu-tenant" is configured with AllowedRegions ["eu-west"]
    When an "OrderPlaced" event for "order-9" is published at "Site A" under AppId "eu-tenant"
    Then eventually "Site B"'s Entity Store should show that "Order" entity "order-9"
    And "Site C" should never receive that event in any peer-sync batch
    # Enforced by a filtering rule at Site A's own Peer Sync Outbox (ADR-061),
    # not a rejected sync attempt or a fold/query-time check.

  Scenario: The Peer Sync Outbox survives an unclean process restart
    Given "Site A" has events queued in its Peer Sync Outbox for "Site B" that have not yet been acknowledged
    When "Site A"'s process terminates uncleanly and is restarted
    Then those queued events should still be present in "Site A"'s Peer Sync Outbox after restart
    And sync to "Site B" should resume from the durable PeerSyncCursor, re-sending only the unacknowledged events

  Scenario: A synced event is folded by the receiving site's own local router, not pre-processed by the sending site
    Given "Site B"'s local schema registry replica is lagging behind "Site A"'s for event type "OrderPlaced" version 2
    When an "OrderPlaced" version 2 event is published at "Site A" and syncs to "Site B"
    Then "Site B" should append it to its own event log exactly as if it had arrived through its own client Inbox
    And "Site B"'s own local fold and schema handling should apply to it, not any pre-processing performed at "Site A"
```
