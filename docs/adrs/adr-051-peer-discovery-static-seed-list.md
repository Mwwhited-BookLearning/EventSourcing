[← ADR index](../07-adrs.md)

# ADR-051: Peer discovery via explicit static seed-peer configuration

Status: Accepted — formalizes [`docs/comparisons/peer-discovery.md`](../comparisons/peer-discovery.md)'s recommendation

Context: `ADR-033`'s gossip/full-mesh replication needs a newly-deployed
peer to learn the address of at least one already-participating peer
before gossip itself can take over. mDNS/DNS-SD were re-examined and
ruled out as LAN-scoped, the wrong tool for cross-internet, cross-site
discovery (`references.md`). `docs/comparisons/peer-discovery.md` weighed
three real options — a static seed-peer list, DNS-based seed discovery,
and a dedicated discovery/rendezvous service — and recommended the
first; direction received this session confirms it explicitly: discovery
happens by **explicit configuration**, not automatic discovery of any
kind.

Decision:
- **Each peer server's own configuration carries a manually-maintained
  list of known-good peer addresses** (`SeedPeers: ["site-a.example.com:
  5001", "site-b.example.com:5001", ...]`) — the same convention
  Cassandra, Consul, and etcd all use for cluster bootstrap. A new peer
  contacts any *one* live seed; `ADR-033`'s gossip protocol discovers the
  rest of the mesh from there. The seed list only ever needs to name a
  subset of currently-live sites, not every site.
- **No automatic discovery mechanism of any kind** — not mDNS/DNS-SD
  (already ruled out), not DNS-based seed discovery, not a dedicated
  rendezvous service. `docs/comparisons/peer-discovery.md`'s Option B
  (DNS `SRV`/`A`-record seed discovery) remains a documented, ready
  upgrade path if the seed set ever starts changing often enough that
  manually redistributing config becomes the operationally annoying
  part — not adopted now, not needed at `ADR-033`'s stated scale (a
  handful of regional sites).
- **Discovery and authentication stay two separate mechanisms,
  deliberately**: this ADR only answers "what address do I dial" — the
  seed list can hand a peer a wrong or malicious address, and
  `ADR-048`'s SPIFFE/SPIRE trust-bundle federation is what actually
  decides whether to trust what answers there. Neither mechanism
  substitutes for the other.

Consequences:
- A seed set change (adding/retiring a site) requires a manual
  configuration update at every *existing* peer that lists seeds — an
  accepted operational cost at this design's stated scale, matching
  Cassandra's and Consul's own production default at cluster sizes far
  larger than this design's scope, not a shortcut avoided by real
  distributed systems.
- Mitigate the "all seeds happen to be down" edge case the standard way:
  seed more than one node per region/site, per Cassandra's own
  guidance — not a new mechanism, an operational practice.
- Resolves and removes the open question `docs/10-open-questions.md`
  tracked for this.
