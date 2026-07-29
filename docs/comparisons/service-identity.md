[← Comparisons index](README.md)

# Service-to-Service Identity: SPIFFE/SPIRE vs. Static OAuth2 Client Credentials vs. Hand-Rolled mTLS

**Un-rejects `references.md`'s SPIFFE/SPIRE entry** — previously
recorded as reference-only: "no multi-workload mesh needing
cross-platform workload attestation... a handful of statically-seeded
OAuth2 clients already covers the actual service-to-service auth
surface at this project's scale. Worth revisiting only if this ever
grows into a real multi-service mesh." That premise no longer holds:
`06-solution-structure.md` now lists a real multi-service topology
(`EventStore.Router`, `.Fold`, `.GraphQL`, `.Sharding`, `.PeerSync`,
`.Streaming`, `.Attachments`, ...), and `ADR-033` introduced genuinely
independent, potentially cross-organization peer servers that need to
mutually authenticate as peers — exactly the scenario the original
rejection said would justify revisiting this.

**Stated requirement driving this comparison**: prove that a request
genuinely originated from a specific internal service or a specific
peer server (`ADR-033`) — not a human/external-client identity question
(`ADR-006` already answers that one, and is unaffected by this
comparison), a *workload* identity question.

## The options

### Option A — SPIFFE/SPIRE (chosen)

| | |
|---|---|
| **Pros** | The real, CNCF-graduated standard for exactly this question. Short-lived X.509-SVIDs (or JWT-SVIDs) issued via **attestation** (verified properties of the workload — no bootstrap secret to leak or rotate manually), unlike `ADR-006`'s static `client_secret` pairs. **Independent trust domains can federate** — two peer servers (`ADR-033`) can mutually verify each other's workload identity by exchanging trust bundles, with no shared root CA — the specific capability this design's cross-site replication scenario actually needs, that a single central OAuth2 IdP can't naturally provide across independent sites. Plugs directly into mTLS (X.509-SVID *is* an mTLS certificate), so this also concretely answers what "hand-rolled mTLS" (Option C) would otherwise have to build from scratch. |
| **Cons** | Real new operational infrastructure — a SPIRE Server + Agents per node, node/workload attestation plumbing, trust-bundle exchange for cross-site federation. Not something `EventStore.DevIdp`'s current in-process, zero-infra dev story gives for free. |

### Option B — Static OAuth2 Client Credentials (`ADR-006`, status quo for this question)

| | |
|---|---|
| **Pros** | Already built, already this design's answer for external/end-user-facing auth — zero additional infrastructure, and genuinely sufficient at the scale the original rejection was written against (a handful of seeded clients). |
| **Cons** | A shared, long-lived `client_secret` is a bootstrap secret that has to be distributed and rotated manually — exactly what attestation-based identity exists to avoid. No natural answer for two independent, possibly cross-organization peer servers (`ADR-033`) to mutually authenticate without a shared central IdP both trust — the gap this comparison exists to close. |

### Option C — Hand-rolled mTLS (no SPIFFE/SPIRE)

| | |
|---|---|
| **Pros** | Same connection-level cryptographic strength as Option A, no new framework dependency. |
| **Cons** | Every piece SPIFFE/SPIRE already standardizes — identity format, attestation, short-lived cert issuance, rotation, cross-trust-domain federation — would have to be designed and built bespoke. This is exactly the "never invent a bespoke mechanism when a real standard already fits" case this design's own conventions warn against. |

## Recommendation

**SPIFFE/SPIRE**, specifically for internal service-to-service calls
and `ADR-033`'s peer-sync authentication — **not** a replacement for
`ADR-006`'s end-user/external-client-facing OAuth2/OIDC, which answers a
different question and is unaffected. The deciding factor is
`ADR-033`'s cross-site federation need specifically: it's the one
requirement none of the other options answer natively, and SPIFFE/SPIRE
answers it as a standard, already-solved capability rather than
something to build.
