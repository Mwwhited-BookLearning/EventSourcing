[← Comparisons index](README.md)

# Peer Discovery: Static Seed-Peer List vs. DNS-Based Seed Discovery vs. Dedicated Discovery/Rendezvous Service

**Raised by:** `ADR-033`, re-raised during a `references.md` review (see
`docs/10-open-questions.md`). **Resolved in
[`ADR-051`](../adrs/adr-051-peer-discovery-static-seed-list.md)** —
Option A (static seed-peer list) below, matching this comparison's own
recommendation.

**Distinct from two related, already-decided questions, on purpose:**
- **Not** `docs/comparisons/peer-sync-topology.md` (`ADR-033`, gossip/
  full-mesh). That comparison answers *"once two peers know about each
  other, how do they exchange data?"* This one answers a question that has
  to be settled *before* that: *"how does a newly-deployed peer learn the
  network address of even one other peer, the first time, with no
  orchestration boundary (Aspire/`docker-compose`) spanning both sites to
  hand it that information?"* Gossip propagates membership knowledge
  perfectly well *after* a node has at least one live contact — it has no
  answer for the very first contact.
- **Not** `ADR-048` (SPIFFE/SPIRE workload identity, peer mutual
  authentication). Discovery answers *"what address do I dial?"*;
  authentication answers *"do I trust what answers at that address?"* — a
  discovery mechanism that returns the wrong or a malicious address is a
  problem SPIFFE/SPIRE's mTLS trust-bundle federation still catches
  (`ADR-048`'s attestation-based mutual verification doesn't complete with
  an impostor), but it's cleaner not to conflate the two concerns in one
  mechanism. Every option below is a way to *find a candidate address*,
  never a way to *trust* it.

**Stated requirement driving this comparison:** a new peer server,
independently deployed at a new site with no shared Aspire/`docker-compose`
orchestration boundary reaching any existing site, must be able to learn
the network address of at least one already-participating peer, so that
`ADR-033`'s gossip/full-mesh gets its first live connection. Everything
after that first connection is `peer-sync-topology.md`'s territory, not
this doc's.

**Already ruled out, re-examined and rejected in `references.md`:**
mDNS/DNS-SD ([RFC 6762](https://datatracker.ietf.org/doc/html/rfc6762)/
[RFC 6763](https://datatracker.ietf.org/doc/html/rfc6763)) are LAN-scoped
service-discovery protocols — mDNS depends on link-local multicast, which
does not cross the public internet or even most routed WAN/VPN boundaries.
A discovery mechanism for independently-deployed, cross-site,
cross-internet peers needs to work over ordinary routed IP, which rules
these out structurally, not just by convention.

## The options

### Option A — Static seed-peer list

Each peer server's own configuration carries a small, manually-maintained
list of known-good peer addresses (`SeedPeers: ["site-a.example.com:5001",
"site-b.example.com:5001", ...]`) — the same convention Cassandra, Consul,
and etcd all use for cluster bootstrap.

| | |
|---|---|
| **Pros** | Simplest possible mechanism — no new infrastructure, no new service to build, deploy, or keep available. Directly matches real, widely-deployed prior art: Cassandra's seed list exists *solely* to bootstrap gossip for a newly-joining node — "seed nodes are not a single point of failure, nor do they have any other special purpose in cluster operations beyond the bootstrapping of nodes" ([DataStax — Internode communications (gossip)](https://docs.datastax.com/en/cassandra-oss/3.x/cassandra/architecture/archGossipAbout.html)), and Consul's `retry_join`/auto-join follows the identical shape ([HashiCorp — Bootstrap a Consul datacenter](https://developer.hashicorp.com/consul/docs/deploy/server/vm/bootstrap)). Once a new peer has successfully gossiped with *any one* seed, gossip itself (`ADR-033`) discovers the rest of the mesh — the seed list only ever needs to name a subset of currently-live sites, not every site. |
| **Cons** | Needs a manual config update, at every *existing* peer that lists seeds, whenever the seed set meaningfully changes (e.g. the only two seeded sites both happen to be down at once for a brand-new joiner with no other config). In practice this is mitigated the same way Cassandra recommends: seed more than one node per region/site, and treat the list as "enough to bootstrap," not "every site that must stay reachable." Doesn't natively expose *current* membership to an operator without also consulting `ADR-033`'s own gossip state. |

### Option B — DNS-based seed discovery

A well-known DNS name resolves, via `SRV` or plain `A` records, to a
rotating/curated set of seed addresses — the peer server queries this name
at startup instead of reading a static list from its own config.

| | |
|---|---|
| **Pros** | Still simple — no new service, just DNS, which every deployment already depends on. Avoids hardcoding IPs in every peer's config: rotating the seed set means updating DNS records once, not redeploying every existing peer. Real, standard prior art on both variants: `SRV` records (`_service._proto.name`, [RFC 2782](https://datatracker.ietf.org/doc/html/rfc2782)) are etcd's documented, production-supported cluster-bootstrap method (`-discovery-srv`, querying `_etcd-server._tcp.<domain>` — [etcd Clustering Guide](https://etcd.io/docs/v3.5/op-guide/clustering/)); a plain rotating `A`/`AAAA` record resolving to a curated seed set is the same convention many CDNs and cluster tools use for "give me a few live entry points, not every entry point." Explicitly distinct from mDNS/DNS-SD (`references.md`'s rejected entry): this is ordinary unicast DNS resolution against a normal, internet-routable domain — no LAN-multicast scoping problem at all. |
| **Cons** | Requires DNS infrastructure this design doesn't otherwise need to operate or trust (who updates the seed records, and how quickly does a change propagate given record TTLs) — a small but real new operational dependency. Still fundamentally the same "bootstrap-only, gossip takes over after" shape as Option A; it only changes *where* the seed addresses are read from, not what they're used for. |

### Option C — Dedicated discovery/rendezvous service

A small, separately-hosted directory service that every peer registers
with on startup and queries to learn currently-live peers — closer to
etcd's own `discovery.etcd.io` bootstrap service or Tailscale's
coordination server (a rendezvous point new nodes contact to learn how to
reach the mesh, not a data-plane participant itself).

| | |
|---|---|
| **Pros** | Supports genuinely dynamic membership — a new site can be added without touching any *existing* peer's configuration or DNS records at all, just registering with the directory. Real prior art: etcd's own discovery-service protocol ("helps a new etcd member discover all other members in the cluster bootstrap phase using a shared discovery URL" — [etcd discovery protocol](https://github.com/ngaut/etcd/blob/v2.2.1/Documentation/discovery_protocol.md)) and Tailscale's coordination server (a rendezvous point that "manages authentication, node authorization, peer discovery... and distributes a network map," while the actual data plane stays peer-to-peer, not routed through it) are exactly this shape, at production scale. |
| **Cons** | A whole new service to design, build, deploy, and keep available — for a design that otherwise reuses `ADR-023`'s existing durable outbox/inbox rather than inventing new infrastructure. That service becomes something every new peer *must* reach to join at all, which is a bootstrap-time dependency this design doesn't currently have anywhere else. Etcd's own docs are explicit that discovery URLs/services are for *initial* bootstrap only, not an ongoing dependency — underscoring that this is more machinery than the underlying problem (find one live contact) actually needs once seeds already solve it. Genuinely justified only past a membership-churn rate neither `ADR-033` nor its stated scope (a handful of regional sites) describes. |

## Recommendation

**Static seed-peer list (Option A), with DNS-based seed discovery (Option
B) as the natural low-cost upgrade if manually redistributing config ever
becomes a real operational pain** — not a dedicated discovery/rendezvous
service (Option C), for the same reason `peer-sync-topology.md` picked
gossip over a hub in the first place: don't introduce a new piece of
infrastructure, with its own availability requirements, to solve a problem
that a much simpler mechanism already covers at this design's actual
stated scale.

`ADR-033` itself scopes this to "a handful of regional sites" and accepts
gossip's `O(n²)` connection cost specifically because site count is small
and not expected to grow into the dozens. A static seed list carries
exactly the same scale assumption, and for the same reason: a handful of
sites means a handful of config lines to seed, reviewed and updated the
same way any other deployment config changes, with no new failure mode
introduced. This is precisely Cassandra's and Consul's own production
default, at cluster sizes far larger than this design's stated scope, not
a shortcut being taken here that real distributed systems avoid.

DNS-based seed discovery (Option B) is worth adopting instead — or
migrating to later — specifically once the seed set starts changing often
enough that redistributing a static list to every existing peer's config
becomes the operationally annoying part; etcd's own production guidance
treats DNS `SRV` discovery as the default for exactly this reason. It's
listed here as a real, ready fallback, not dismissed, but nothing in
`ADR-033`'s stated scope justifies starting there over the simpler option.

A dedicated discovery/rendezvous service (Option C) is explicitly **not**
recommended at this design's current scale: it solves a membership-churn
problem ("sites come and go often enough that touching every existing
peer's config is untenable") this design doesn't have, at the cost of a
new always-available service this design would then depend on just to let
a new site join at all — a cost `peer-sync-topology.md` already rejected
in a different guise (a hub is itself a new single point of failure to
reach the rest of the mesh). Revisit only if the number of sites, or the
rate at which sites are added/retired, grows enough that Option A/B's
manual-update cost becomes the actual bottleneck — not preemptively.

**Once discovery hands a peer a candidate address, `ADR-048`'s SPIFFE/SPIRE
trust-bundle federation is what actually decides whether to trust what
answers there** — none of the three options above are an authentication
mechanism, and none should become one; keeping "find an address" and
"trust an address" as two separate mechanisms is deliberate, not an
oversight, per this design's own "disambiguate related-but-distinct
concerns" convention.
