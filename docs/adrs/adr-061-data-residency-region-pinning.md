[← ADR index](../07-adrs.md)

# ADR-061: Data residency — per-`AppId` allowed regions, enforced at replication/sharding assignment

Status: Accepted

Context: `docs/10-open-questions.md` asked whether a tenant should be
able to constrain which sites/regions its shards replicate to. Direction
received this session: **a real requirement, not speculative** — some
tenants will be regionally bound by regulation (e.g., an EU health
tenant needing GDPR-adjacent data-residency guarantees, a state/national
public-sector tenant with sovereignty rules). Real prior art checked
before designing anything bespoke: [Azure Cosmos DB's data-residency
configuration](https://learn.microsoft.com/en-us/azure/cosmos-db/data-residency)
(explicit region allowlisting for replication) and [MongoDB Atlas Global
Clusters](https://www.mongodb.com/docs/atlas/architecture/current/deployment-paradigms/global-data/)
(a region/zone folded directly into the shard key) both converge on the
same shape this ADR adopts: **region is a real dimension of the sharding/
replication assignment, not a separate bolt-on system.**

Decision:
- **Every configured peer (`ADR-051`'s `SeedPeers`) gains a `Region` tag**
  (e.g. `"eu-west"`, `"us-east"`) — a small, deployment-time
  configuration addition, not a new discovery mechanism (`ADR-051`'s
  "discovery and authentication stay separate" reasoning extends here:
  region tagging is metadata about a peer, not how it's found or
  trusted).
- **A new per-`AppId` `AllowedRegions` list** (`docs/data/schema-
  registry.md`, alongside `AppTrustRoot`'s existing per-`AppId`
  configuration shape, `ADR-044`) — e.g. `AllowedRegions: ["eu-west",
  "eu-central"]`. Absent, an `AppId` is unconstrained (today's behavior,
  unchanged) — this is purely additive.
- **Enforced at `ADR-033`'s peer-sync outbox, not at fold/query time**:
  when an event belonging to a region-constrained `AppId` is queued for
  outbound gossip sync, the outbox filters candidate destination peers
  to only those tagged with one of the `AppId`'s `AllowedRegions` —
  the event is simply never included in a sync batch bound for a
  disallowed site. This is the same enforcement point `ADR-033`'s
  existing peer-sync outbox already owns, not a new mechanism layered
  beside it.
- **`ADR-034`'s `ShardKey = EntityType` is unchanged** — region is a
  *replication-destination* constraint, not a new sharding dimension.
  Unlike MongoDB Atlas's approach (region folded directly into the shard
  key), this design keeps `EntityType`-based sharding exactly as `ADR-
  034` decided and layers the region constraint onto *where a shard's
  replicas are allowed to live*, since this design's shards are already
  the right granularity for replication scope (`ADR-034`'s own stated
  consequence: "a type-based shard boundary is also a natural
  replication-scope boundary") — no reason to add a second dimension to
  the key itself when the constraint composes cleanly onto the existing
  one.
- **Honest, named tension with `ADR-033`'s minimum-replication-factor-
  of-2 requirement, not silently glossed over**: a tenant restricted to
  a single region satisfies both requirements only if that region
  actually has ≥2 live sites tagged with it. A tenant restricted to a
  region with only one deployed site cannot simultaneously get
  `ADR-033`'s fault-tolerance guarantee *and* residency compliance —
  **residency wins** (it's a regulatory hard constraint; fault tolerance
  is a design preference), and the deployment is responsible for
  ensuring ≥2 sites exist in any region a tenant might restrict to, or
  knowingly accepting single-site risk for that tenant. This is stated
  as an operational responsibility a deployment must satisfy, not a gap
  this ADR silently introduces.

Consequences:
- Resolves `docs/10-open-questions.md`'s data-residency row.
- `docs/data/schema-registry.md` gains `AllowedRegions` on the per-
  `AppId` configuration record; `06-solution-structure.md`'s `SeedPeers`
  configuration sketch gains a `Region` field per entry.
- No new component — `ADR-033`'s peer-sync outbox gains one filtering
  rule at the point it already selects sync destinations.
- A tenant's `AllowedRegions` is visible/auditable configuration, not
  buried logic — consistent with this design's broader preference for
  configuration over code for deployment-shaped decisions (`ADR-058`'s
  rate-limit values are the same shape).
