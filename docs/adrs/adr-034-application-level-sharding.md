[← ADR index](../07-adrs.md)

# ADR-034: Application-level sharding by `EntityType`

Status: Accepted

Context: The Entity Store (`docs/data/entity-store.md`) needs a rule for
computing `ShardKey`. See
[`docs/comparisons/sharding-strategy.md`](../comparisons/sharding-strategy.md)
for the full entity-type-based vs. hash-based consistent-hashing
comparison this ADR is built on.

Decision:
- **`ShardKey = EntityType`** (or a small, fixed mapping from type to
  shard, for cases where a single type still needs subdividing) — the
  default and only mechanism in v1.
- **Distinguish log partitioning from entity-store sharding explicitly**:
  the event log (`docs/data/event-log.md`) may remain a single append
  log — or be partitioned per stream for a specific deployment's needs —
  entirely independently of how the Entity Store is sharded. These have
  different consistency implications (the log's `SequenceNumber` ordering
  is global and total; sharding the *read* side doesn't need to
  preserve that) and this design doesn't conflate them.
- **Cross-shard queries need a coordinator that fans out and merges** —
  a GraphQL resolver (`ADR-037`) spanning entity types
  that live on different shards issues one query per shard and merges
  results, rather than assuming a single-shard query plan always
  suffices.

Consequences:
- One very hot event type can dominate its shard with no built-in way to
  split it further — an accepted v1 limitation, not solved here (see the
  comparison doc's recommendation for the documented upgrade path: hash-
  shard *that specific type* internally, without changing the default
  for everything else).
- Trivial to test in BDD scenarios (`08-build-plan.md`) — a whole type's
  data is deterministically in one place, no ring-topology math needed to
  reason about where a given test fixture's data lives.
- Works cleanly with `ADR-033`'s replication: a shard (one `EntityType`'s
  worth of data) is the unit that gets replicated to at least two sites,
  not the whole store as one undifferentiated blob — a type-based shard
  boundary is also a natural replication-scope boundary.
