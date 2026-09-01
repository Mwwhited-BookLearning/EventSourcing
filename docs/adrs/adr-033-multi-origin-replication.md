[← ADR index](../07-adrs.md)

# ADR-033: Multi-origin replication — gossip topology, fault-tolerant peer-sync outbox/inbox

Status: Accepted

Context: This design has been explicitly single-store since `ADR-001`'s
original framing. Integrating `docs/design-docs/09` and direct requirement
("at least two separate servers... locally/regionally fault tolerant")
brings replication into real scope. See
`docs/comparisons/peer-sync-topology.md` for the full topology comparison
this ADR is built on — gossip/full-mesh was chosen there specifically
because it's the only option that delivers "any single site can go dark
without losing data or availability" without introducing a second single
point of failure. Also governed by `CLAUDE.md`'s standing requirement:
any outbox this design introduces must be fault/abend/restart-tolerant.

Decision:
- **Topology: gossip/full-mesh** (`docs/comparisons/peer-sync-topology.md`).
  Every server periodically exchanges with every other known server;
  each ends up holding, eventually, everything it's configured to
  replicate.
- **Minimum replication factor of 2, stated as a deployment requirement,
  not assumed from the topology alone.** Every shard (`ADR-034`, the
  sharding ADR) must be configured with at least two sites replicating
  it, ideally in different regions — the topology comparison flagged
  this explicitly as a distinct decision from "which topology," and this
  ADR is where it becomes real.
- **`OriginId` + `LogicalClock` travel with every event**, extending
  `ADR-029`'s single-origin `OccurredAt` ordering to a genuinely
  cross-origin-safe one — wall-clock timestamps alone are insufficient
  once multiple independent sites write concurrently (`docs/design-docs/09
  §9.3`'s own stated reason, adopted here directly). `LogicalClock` is a
  Hybrid Logical Clock (HLC) value, not a plain vector clock — bounded
  size regardless of origin count, unlike a true vector clock that grows
  with the number of origins ever seen.
- **Peer Sync Outbox/Inbox reuses the exact same durable transport
  primitive `ADR-023`'s client-facing Inbox already established** —
  client→server, server→server, and (once `ADR-039`'s MVVM client exists)
  server→client are three relationships over one mechanism, not three.
  A synced event lands in the receiving server's event log exactly as if
  it arrived from its own client inbox — sync itself performs no
  routing, schema validation, or projection; the receiving server's own
  local router/projector handles it with whatever schema/registry
  knowledge that server currently has (`ADR-030`'s multi-tenant framework
  applies identically regardless of which site an event originated at).
- **Fault/abend/restart tolerance, concretely**: the Peer Sync Outbox and
  Inbox are durable tables (not in-memory queues) — an unclean process
  termination loses nothing queued, because nothing queued was ever only
  in memory. A per-peer sync cursor
  (`PeerSyncCursor { PeerId, LastReceivedSequenceNumber,
  LastAckedSequenceNumber, LastSyncAttemptAt, LastSyncSuccessAt }`) is the
  resumption point after a restart — sync picks up exactly where it left
  off, the same "durable checkpoint, not memory" discipline `ADR-015`'s
  `ProjectionCheckpoint` already established for a different consumer.
- **Efficient catch-up via Merkle tree comparison**: a server rejoining
  after a long disconnection exchanges hash-tree summaries of event
  ranges with each peer rather than replaying everything from zero —
  standard Dynamo/Cassandra-style anti-entropy, extending the same
  hashing discipline `ADR-019`'s `ChainHash` already established (a
  different application: ranges of the log for catch-up efficiency, not
  tamper evidence).
- **Cross-server divergence resolves via the exact same mechanism as
  same-server concurrent writes** — `ADR-024`'s `ConflictFlag`, triggered
  by a sync-delivered event conflicting with a local one instead of a
  same-server concurrent submission. No second resolution system.

Consequences:
- Read-after-write consistency was already not attempted within a single
  store (`ADR-015`'s consequences); replication makes this more visible,
  not newly true — a client reading from a different site than it wrote
  to may see stale data for a real, now-cross-region window, not just a
  local-projector-lag window.
- `O(n²)` connections/redundant transfer as site count grows is an
  accepted, explicitly-stated cost (`docs/comparisons/peer-sync-
  topology.md`) — fine for a handful of regional sites, worth revisiting
  only if site count grows into the dozens.
- The Schema Registry (`docs/data/schema-registry.md`) is now itself
  replicated data, not synchronously-consistent side infrastructure — a
  client emitting an event at a schema version its local site's registry
  hasn't heard about yet still gets a self-sufficient, version-tagged
  event (`ADR-020`'s live upcast validation and `ADR-027`'s
  materialization already handle a lagging *version*; this extends the
  same tolerance to a lagging *registry replica*, not a new mechanism).
- `EntityStoreRow.LastAppliedOriginId` (`docs/data/entity-store.md`)
  finally has a real consumer — diagnosing which site's write most
  recently won a fold, useful when investigating a `ConflictFlag`.

**Additive note (`ADR-102`)**: this mechanism was verified genuinely
cross-provider for the first time — two peers running different
`Host.<Provider>` artifacts (a real SQL Server Testcontainer and a real
SQLite file, plus a live three-node mesh also including Postgres,
orchestrated together under `EventStore.AppHost`) — confirming directly
that `PeerSyncClient`/`PeerSyncReceiver`'s plain-HTTP-JSON transport
never assumed same-provider peers. No change to this ADR's own Decision;
`ADR-102` is verification, not revision.

**Compliance note** (a proving-ground compliance review, this session):
the minimum-replication-factor-of-2 requirement is the concrete
mechanism satisfying HIPAA's Contingency Plan standard (45 CFR
§164.308(a)(7) — data backup, disaster recovery, and emergency-mode-
operation procedures for electronic protected health information), not
an incidental side effect of fault tolerance; the same multi-site
durability generalizes to the data-backup/disaster-recovery
expectations most proving-ground domains' compliance frameworks state
in some form.
