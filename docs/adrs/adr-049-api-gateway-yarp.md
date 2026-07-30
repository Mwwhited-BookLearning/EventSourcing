[← ADR index](../07-adrs.md)

# ADR-049: API Gateway (YARP) as the single external entry point

Status: Accepted — reverses `references.md`'s prior YARP rejection

Context: `references.md` previously rejected YARP: "no gateway/reverse-
proxy tier exists in this design — each `EventStore.Host.<Provider>`
is a single deployable serving its own endpoints directly." That
premise no longer holds. `06-solution-structure.md` now lists multiple,
independently-addressable external surfaces — the GraphQL Gateway
(`ADR-037`), attachment retrieval (`ADR-032`), streaming channel
playback (`ADR-031`), ticket issuance/introspection (`ADR-040`), and
OAuth token endpoints (`ADR-006`) — exactly the shape the real **API
Gateway pattern** ([microservices.io](https://microservices.io/patterns/apigateway.html))
exists for: a single entry point in front of many services, so a
client interacts with what looks like one API rather than needing to
know N separate addresses.

Decision:
- **[YARP](../libraries/dotnet/yarp.md)** (Microsoft's own reverse-proxy library — a first-party fit
  per `ADR-041`) sits in front of every external-facing surface listed
  above, routing by path/host to the right internal service.
- **External TLS termination and `ADR-006`/`ADR-017`/`ADR-040`
  authentication happen at the gateway** — a caller authenticates once,
  against one address; the gateway forwards the validated identity
  onward. Internal gateway-to-service calls use `ADR-048`'s SPIFFE/SPIRE
  workload identity, not a second copy of external auth — the gateway
  is the one boundary where "external caller identity" (`ADR-006`)
  and "internal workload identity" (`ADR-048`) meet and hand off.
- **Not a Backends-for-Frontends split** — one gateway, not one per
  client type. Nothing in this design yet has different client
  categories needing meaningfully different response shapes from the
  same underlying services; revisit only if that changes.

Consequences:
- A new moving part to deploy and keep available — the classic,
  explicitly-named cost of this pattern (a single gateway is also a
  potential single point of failure if not itself made redundant, same
  concern `ADR-033`'s hub-and-spoke-vs-gossip comparison already
  reasoned through for a different component).
- `references.md`'s YARP entry moves from "reference-only, rejected" to
  "adopted" — the same un-reject pattern already used repeatedly this
  session.
- `01-c4-architecture.md` needs a Gateway container added in front of
  the existing containers it currently shows as directly external-
  facing — not done this pass, flagged as outstanding propagation work
  (`CLAUDE.md`).

**Compliance note** (a proving-ground compliance review, this session):
centralizing external TLS termination and authentication at one gateway
is the concrete managed-interface NIST SP 800-53 Rev. 5's SC-7
(Boundary Protection) control calls for — connecting to external
networks only through boundary protection devices like gateways that
monitor and control all external traffic, rather than each service
terminating its own external-facing connection independently.
