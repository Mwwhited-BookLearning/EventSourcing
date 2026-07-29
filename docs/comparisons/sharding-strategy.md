[← Comparisons index](README.md)

# Sharding Strategy: Entity-Type-Based vs. Hash-Based Consistent Hashing

**Gates:** the queued sharding ADR. **Raised by:** `docs/design-docs/09
§9.2`, which recommends consistent hashing but leaves the choice open
(`14-open-questions.md`).

## The fork

`EntityStoreRow.ShardKey` (`docs/data/entity-store.md`) needs a rule for
computing it from `EntityId`/`EntityType`. Two real options:

### Option A — Shard by `EntityType`

Every entity of a given type lands on the same shard; `ShardKey =
EntityType` (or a small, fixed mapping from type to shard).

| | |
|---|---|
| **Pros** | Trivial to reason about — "where's this entity" never needs a lookup table, just its type. Trivially testable in BDD scenarios (`08-build-plan.md`'s stated integration-test discipline) since a whole type's data is deterministically in one place. No rebalancing math when scaling shard count — you just move a type wholesale. |
| **Cons** | Load distribution is only as even as the natural distribution of entity types and their write/read volume — one very hot type (e.g. `OrderPlaced` in a retail app) can dominate a shard while others sit idle, with no way to split a single type across two shards without a second mechanism layered on top. Doesn't scale past "as many effective partitions as you have types with meaningfully different volume." |

### Option B — Hash-based consistent hashing

`ShardKey = ConsistentHash(EntityId) % RingSize`; entities of the same
type spread across shards by hash of their full ID.

| | |
|---|---|
| **Pros** | Even load distribution regardless of which types happen to be hot — the standard answer for horizontal write scaling (this is the Dynamo/Cassandra-style approach). Consistent hashing specifically (vs. naive `hash % N`) minimizes reshuffling when the shard count changes — only the entities whose hash falls in the affected ring segment move, not everything. |
| **Cons** | "Where's this entity" now requires computing a hash and consulting ring topology, not just reading its type — a real, if small, indirection cost on every read/write. Harder to reason about in a worked-example/teaching context (`README.md`'s stated purpose) — a reader has to understand consistent hashing to understand where their data lives, vs. reading it directly off the entity's own type. Cross-shard queries for "all entities of type X" now fan out across every shard instead of hitting one, the opposite of Option A's advantage. |

## Recommendation

**Shard by `EntityType` as the v1 default**, with consistent hashing
recorded as the documented upgrade path for a type that turns out to be
genuinely too hot for one shard (at which point that *specific* type
could hash-shard internally, without changing the default for every
other type). This matches design-docs' own framing of Option A as "a
simpler alternative... easier to reason about and set up for BDD tests,
at the cost of less even load distribution" — and this project's stated
teaching purpose weighs toward the option a reader can understand by
reading the entity's own `EntityType`, not one that requires understanding
a ring topology to explain where data lives. Revisit if a real deployment
scenario makes one type's write volume the actual bottleneck — not
before, since that's exactly the failure mode Option A's "cons" describes
and Option B exists to solve.
