[← ADR index](../07-adrs.md)

# ADR-036: DID + UCAN for offline self-attestation, exchanged via OAuth Token Exchange (RFC 8693)

Status: Accepted — un-rejects DID/UCAN/RFC 8693 from `references.md`,
now that `ADR-035` creates the need they were rejected for lacking.

Context: `references.md` previously recorded UCAN, DID, and OAuth Token
Exchange as *considered and rejected* — correctly, at the time, since no
actor in this design needed offline, authority-free credential
attenuation. `ADR-035`'s non-authoritative capture changes that: a field
actor capturing data while disconnected, whose authority can't be
verified until connectivity returns, is now a real scenario this design
supports.

**Corrected, 2026-08-12, found by an independent design-compliance
audit**: the server-initiated token-exchange mechanism this Decision
describes below (a disconnected client submits a raw UCAN alongside its
event; a server-side exchange step later validates it and mints an
ordinary bearer JWT carrying `delegation_chain_ref`) was never built —
confirmed directly, zero hits for a `/oauth/token` exchange call or
`delegation_chain_ref` handling anywhere in `EventStore.Router`/
`EventStore.Inbox`. What IS built (`EventStore.Ucan/UcanValidator.cs`) is
a different, correctly-cross-referenced mechanism serving `ADR-043`/`044`
instead: a client-signed `UcanDelegation` JWT, self-verified (signature +
proof-chain/trust-root check), never submitted as a raw UCAN alongside a
non-authoritative capture event the way this ADR's own scenario
describes. `ADR-035`'s own `AttestedClaims`/self-attestation half is
genuinely built (`ADR-035`'s own Decision, unaffected) — it's this ADR's
*specific* DID/UCAN self-attestation issuance flow, not the general
non-authoritative-capture mechanism, that has no real implementation.
This was honestly narrated as a gap in `docs/08-build-plan.md`'s own
text already, but never carried this correction note here until now.

Decision:
- **DID (Decentralized Identifier)** proves cryptographic control of an
  identifier — "the holder of this key says they are `did:key:z6Mk...`"
  — deliberately **not** proof that identifier maps to a real-world
  vetted role. This is the correct primitive precisely because it
  matches `ADR-035`'s `unattested` starting state exactly: a claim of
  identity, not a verified one.
- **UCAN (User Controlled Authorization Network)** proves a chain of
  delegated capability, entirely offline-verifiable — no central
  authority needs to be reachable at invocation time. A UCAN invocation
  serves directly as an `AttestedClaims` payload (`ADR-035`) —
  cryptographically structured rather than free-text, with the
  delegation chain itself becoming evidence attached to the event for
  later review.
- **Server-side token exchange at ingestion, not client-side
  pre-exchange** — given the offline-capture requirement, a
  disconnected client cannot reach an OIDC provider to exchange anything
  before connectivity returns. The client submits the raw UCAN
  alongside the event immediately (`ADR-023`'s Inbox persists it
  regardless); the exchange happens once the event reaches a server that
  can reach the identity provider:
  ```
  POST /oauth/token
  grant_type=urn:ietf:params:oauth:grant-type:token-exchange
  subject_token=<UCAN invocation>
  subject_token_type=urn:your-org:token-type:ucan
  requested_token_type=urn:ietf:params:oauth:token-type:jwt
  ```
  The identity provider (or a small bridge in front of it) validates the
  UCAN chain, then mints an ordinary bearer JWT — same issuer, same
  signing key, same shape every other endpoint in this design already
  expects (`ADR-006`). No downstream service needs to understand what a
  UCAN or DID even is — the encapsulation goal that justified adopting
  this over a bespoke scheme.
- **The JWT carries `provenance`/`authority_status`/`delegation_chain_ref`
  claims**, flowing directly into `StoredEvent`'s `AttestedClaims`/
  `AuthorityStatus` (`docs/data/event-log.md`) — the JWT *is* the
  attestation artifact, not a separate thing built alongside it.
  `delegation_chain_ref` stores a hash/reference to the full chain (as an
  `ADR-032` binary attachment, reusing that content-addressed mechanism
  rather than inventing a second blob-storage path), not the whole chain
  inline in every JWT.
- **A valid token-exchange result is not the same as authority approval**
  — a syntactically/cryptographically valid UCAN only proves the exchange
  happened correctly; it does not upgrade `AuthorityStatus` to `accepted`.
  That only happens via `ADR-035`'s explicit `authorityDecision` event.
  Cryptographic validity and authoritative approval are kept deliberately
  separate.

Consequences:
- `references.md`'s "reference-only, rejected" entries for UCAN, DID, and
  RFC 8693 need updating to reflect this adoption — the earlier rejection
  reasoning was correct *given the actors that existed then*, not wrong.
- UCAN chain validation happens once, at the token-exchange step — the
  Inbox, Router, and fold step (`ADR-023`/`ADR-021`) never touch UCAN/DID
  semantics directly; they see a bearer JWT like any other and read
  `AuthorityStatus` off its claims (or off the event, once persisted).
- This also solves the replication-side offline problem for free: UCANs
  are self-verifying, so a receiving peer server (`ADR-033`) — even one
  that's itself disconnected from the identity provider — can validate a
  captured event's attestation chain without calling back to anything,
  consistent with "no guaranteed connectivity, no central authority
  reachable at capture time." A plain OAuth token alone would not survive
  that scenario.
- `projections-client`/other internal service identities (`ADR-015`)
  are unaffected — this mechanism is specifically for *external,
  unverifiable-at-capture-time* actors, not for the framework's own
  internal service-to-service auth, which stays exactly as `ADR-006`/
  `ADR-017` already designed it.

**Compliance note** (a proving-ground compliance review, this session):
self-attestation proves *identity*, not *permissibility* — this ADR has
no bearing on, and shouldn't be confused with, **OFAC sanctions
screening** or **BSA Suspicious Activity Report (SAR)** filing, both
real requirements for the digital-identity/KYC proving-ground domain
(an actual build target). A cryptographically valid DID/UCAN can belong
to a sanctioned party just as easily as a legitimate one — screening
against a prohibition list, and any SAR filing decision, is separate
business logic layered on top of a verified identity, not something
self-attestation itself resolves. Tracked as an open question: whether
this belongs as a framework-level extensibility seam or purely
domain/application logic.
