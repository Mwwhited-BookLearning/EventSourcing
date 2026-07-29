[← Comparisons index](README.md)

# Peer-Sync Topology: Gossip/Full-Mesh vs. Hub-and-Spoke vs. Leaderless Pull

**Gates:** the queued replication ADR. **Raised by:** `docs/design-docs/09
§9.4.1`, left open in `14-open-questions.md`.

**Stated requirement driving this comparison:** at least two independent,
full copies of the data, on physically separate servers — and specifically
**both local (single-node/single-rack) and regional (whole-site/region)
fault tolerance**: losing one server, or one entire site, must not lose
data or availability, as long as at least one other replica survives
elsewhere. This is a firmer requirement than design-docs' own "no
guarantee any two replicas are in the same state at any given time"
framing (`09 §9.1`) — that's still true (eventual consistency, not
synchronous replication), but the *topology* choice below now has to
actually deliver "any single site can go dark and the system keeps
serving from elsewhere," not just "servers eventually agree."

## The options

### Option A — Gossip / full mesh

Every server periodically exchanges with every other server it knows
about; each server independently ends up holding (eventually) a full
copy of everything.

| | |
|---|---|
| **Pros** | Directly satisfies the stated requirement: every site holds a full replica, so any single site — or several — going dark still leaves every surviving site fully functional, reads and writes, with no single point of failure anywhere in the topology. Most resilient to *any* single node/link failure, by construction. The model gossip/anti-entropy protocols (Dynamo-style) were built for — "eventually converges, no guaranteed shared state at any instant" is exactly this topology's native behavior, not a property bolted onto it. |
| **Cons** | `O(n²)` connections and redundant transfer as node count grows — fine for a handful of regional sites, a real cost if the site count ever grows into the dozens. More moving parts to monitor (every server watching every other) than a hub. |

### Option B — Hub-and-spoke

One or a few relay nodes; every server syncs through a hub rather than
directly with every peer.

| | |
|---|---|
| **Pros** | Simpler, cheaper on connections — `O(n)` not `O(n²)`. Each server still retains its own durable inbox/outbox, so a hub being briefly unreachable is a *delay*, not data loss (per design-docs' own framing) — the spoke just catches up once the hub is reachable again. |
| **Cons** | **Fails the stated regional-fault-tolerance requirement directly if the hub itself is regional infrastructure** — every spoke's ability to reach every *other* spoke's data depends on that one hub (or hub region) being reachable. A hub outage doesn't lose data (spokes keep their own durable state), but it does mean two surviving spokes in different regions can't sync *with each other* until the hub recovers — a real availability gap the requirement above rules out, not just a "delay." Would need the hub itself made multi-region/highly-available to satisfy the requirement, which mostly just re-introduces gossip's problem one layer up (now the hubs need to gossip with each other). |

### Option C — Leaderless pull

Each server periodically asks each known peer "give me everything after
sequence N," rather than peers pushing.

| | |
|---|---|
| **Pros** | Simplest failure handling — a down peer just means no new data yet from that peer, no dead-letter/retry queue needed on the sender side (there is no "sender" in the push sense). Fits this design's existing pull-oriented dispatcher pattern (Follow's own poll loop, `ADR-010`) — same mental model reused, not a new one. |
| **Cons** | Still fundamentally a full-mesh topology under the hood (every server needs to know about and periodically pull from every other server holding a copy it wants) — so it shares full-mesh's `O(n²)` connection-counting concern without gossip's push-driven propagation speed; a server that should have a copy but hasn't gotten around to pulling from the right peer yet can lag further behind than a push model would let it, especially right after a regional outage resolves and there's a backlog to catch up on. |

## Recommendation

**Gossip/full-mesh**, specifically because it's the only option of the
three that satisfies the stated regional-fault-tolerance requirement
*without* introducing a second single point of failure (the hub) that
would itself need the same fault-tolerance treatment. The connection-count
cost (`O(n²)`) is a real, accepted trade — but for a *regional* replication
topology (a handful of sites, not hundreds), that cost is small in
absolute terms; revisit only if the site count ever grows far enough for
it to matter in practice, not preemptively. This also directly satisfies
the CLAUDE.md-recorded standing requirement that any outbox this design
introduces must be fault/abend/restart-tolerant: gossip's per-server
durable outbox/inbox (`docs/design-docs/09 §9.4`) is what makes "any
single site can go dark and come back" actually safe, not just
topologically plausible.

**Concretely, for the queued replication ADR**: a minimum replication
factor of 2 is not automatic just from picking gossip — it needs to be a
stated deployment requirement (every entity's shard, `sharding-
strategy.md`, must have at least 2 sites configured to replicate it), not
assumed to fall out of the topology choice alone. Flagging this now so
the ADR doesn't silently treat "gossip topology chosen" as "replication
factor guaranteed" — they're related but distinct decisions.
