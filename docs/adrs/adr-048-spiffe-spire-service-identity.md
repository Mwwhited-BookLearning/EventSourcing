[← ADR index](../07-adrs.md)

# ADR-048: SPIFFE/SPIRE for internal service-to-service and peer-sync identity

Status: Accepted — reverses `references.md`'s prior SPIFFE/SPIRE rejection

Context: `references.md` previously rejected SPIFFE/SPIRE: "no
multi-workload mesh needing cross-platform workload attestation... a
handful of statically-seeded OAuth2 clients already covers the actual
service-to-service auth surface at this project's scale." That premise
no longer holds — `06-solution-structure.md` now lists a real
multi-service topology, and `ADR-033` introduced genuinely independent
peer servers needing to mutually authenticate across what may be
separate administrative/organizational boundaries, exactly the scenario
the original rejection named as the reason to revisit. Full comparison
in [`docs/comparisons/service-identity.md`](../comparisons/service-identity.md).

Decision:
- **[SPIFFE/SPIRE](../libraries/dotnet/spiffe-spire.md) issues workload identity for this framework's own
  internal services** (`EventStore.Router`, `.Fold`, `.GraphQL`,
  `.Sharding`, `.PeerSync`, `.Streaming`, `.Attachments`) — each gets a
  SPIFFE ID (`spiffe://<trust-domain>/eventstore/<service-name>`),
  delivered as a short-lived X.509-SVID via SPIRE's attestation (no
  bootstrap secret to distribute or rotate manually, unlike `ADR-006`'s
  `client_secret` model).
- **`ADR-033`'s peer-sync authentication moves onto SPIFFE/SPIRE's
  cross-trust-domain federation** — two independent peer servers
  exchange SPIFFE trust bundles and mutually verify each other's
  workload identity via mTLS, with no shared central IdP or root CA
  required. This is the specific capability that closes the gap
  `ADR-006`'s single, centrally-issued OAuth2 model can't reach: two
  sites under different administrative control, authenticating each
  other directly.
- **`ADR-006`'s OAuth2/OIDC is unaffected and stays exactly as
  designed** — SPIFFE/SPIRE answers a different question (which
  *workload* is calling) from `ADR-006` (which *user/external client*
  is calling). A request into the system from an external caller is
  still bearer-JWT-plus-DPoP-authenticated (`ADR-006`/`ADR-017`); once
  inside, calls between this framework's own services (and between peer
  servers) are additionally mTLS-authenticated via SPIFFE identity.
- **X.509-SVID is this design's concrete mTLS mechanism** — resolving
  the "hand-rolled mTLS" option this design would otherwise have needed
  to design from scratch for any service-to-service transport-level
  authentication.

Consequences:
- **Real new operational infrastructure**: a SPIRE Server + Agent per
  node, workload/node attestation configuration, trust-bundle exchange
  for cross-site federation. `EventStore.DevIdp`'s current zero-infra,
  in-process dev story is unaffected for *external* auth, but local
  dev/Aspire orchestration (`ADR-026`) needs a SPIRE Server + Agent
  added to the AppHost for realistic local testing of inter-service
  calls — not designed further here.
- `references.md`'s SPIFFE/SPIRE entry moves from "reference-only,
  rejected" to "adopted" — done this pass, the same "un-reject once a
  real need appears" pattern already used for content-addressable
  storage (`ADR-032`) and UCAN/DID/RFC 8693 (`ADR-036`).
- `06-solution-structure.md` needs each internal service project
  annotated with its SPIFFE ID convention, and `ADR-033`'s peer-sync
  sequence needs its mutual-auth step described in SPIFFE terms — not
  done this pass, flagged as outstanding propagation work (`CLAUDE.md`).
- Does not affect `ADR-043`'s UCAN-based delegation, `ADR-044`'s
  `AppTrustRoot`, or `ADR-047`'s federated-IdP claims augmentation — all
  three are about *user/application* identity and permission, a
  different axis from the *workload* identity this ADR addresses.

**Compliance note** (a proving-ground compliance review, this session):
this is a direct implementation of NIST SP 800-207's Zero Trust
Architecture model, whose own follow-on SP 800-207A names SPIFFE
specifically as reference application-identity infrastructure; it's
also the concrete mechanism an ITAR/CMMC-scoped deployment (NIST SP
800-171) would point to for verifying every internal workload's
identity rather than trusting network location, relevant given
`docs/domains/itar-export-controlled-defense-data.md`'s coverage.
