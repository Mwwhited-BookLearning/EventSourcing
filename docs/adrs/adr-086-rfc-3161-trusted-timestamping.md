[← ADR index](../07-adrs.md)

# ADR-086: RFC 3161 trusted timestamping for `ADR-066` signatures and `ADR-068` litigation exports

Status: Accepted

Context: `docs/10-open-questions.md`'s clock-authority question left one
residual after `ADR-083`'s monotonic-timer resolution: should RFC 3161
timestamping be adopted for `ADR-066` digital sign-offs and `ADR-068`
litigation exports? Direction received this session, as a general
principle: **prefer a real RFC/web standard over a custom mechanism
whenever one is genuinely available** — restating and applying this
project's own existing "never invent a bespoke mechanism when a real
standard already fits" convention (`CLAUDE.md`), not a new one. Verified:
[RFC 3161](https://datatracker.ietf.org/doc/html/rfc3161), the Internet
X.509 PKI Time-Stamp Protocol — a Time Stamping Authority (TSA) returns
a signed `TimeStampToken` over a submitted hash, proving that hash (and
therefore whatever it's a hash of) existed at or before a specific time,
independent of any party to the transaction's own clock. Broadly
supported, standards-track, exactly the primitive both cases need.

Decision:
- **Yes, adopt RFC 3161 for both cases.** A real, available standard
  fits precisely — this design's own `ChainHash`/`ContentHash` primitives
  already produce exactly the hash a TSA needs to timestamp; no bespoke
  timestamping mechanism should be built when RFC 3161 already exists
  and already integrates with a hash this design computes anyway.
- **`ADR-066`'s `Signature` gains an optional `RFC3161Timestamp`**
  (`docs/data/event-log.md`) — the TSA's `TimeStampToken`, obtained over
  a hash of the signed event's `ChainHash` (not the event's `Payload`
  directly, since `ChainHash` already transitively commits to the full
  event content, `ADR-019`). Optional because not every `Signature`-
  requiring event type needs third-party-verifiable timing — a
  deployment enables it per event type via the same `EventTypeDefinition`
  configuration surface `RequiredSignature` already uses, not a global
  switch.
- **`ADR-068`'s litigation export bundle gains an RFC 3161 timestamp over
  its own root hash** — the export mechanism already computes a root
  hash "proving the export is a complete, unaltered copy of that chain";
  timestamping that root hash at export time proves *when* the export
  was made, independent of trusting the exporting party's own system
  clock — directly strengthening exactly the "independent of trusting
  the exporting party" property `ADR-068` already states as its goal.
- **A TSA is a pluggable dependency, not a hardcoded vendor** — this
  design already treats external trust infrastructure as swappable
  (`ADR-057`'s `IErasureKeyStore`, `ADR-041`'s configuration-sourced
  secrets); an `ITimestampAuthorityClient` seam, registered per
  `ADR-059`'s composition-root model, lets a deployment point at any
  RFC-3161-compliant TSA (a public one, or an internally-operated one
  for a regulated deployment that can't send hashes to a third party).
- **Verification needs no new mechanism** — RFC 3161 tokens are verified
  against the TSA's own published certificate chain, the same X.509
  trust-chain verification this design's auth stack already performs
  elsewhere (`ADR-006`); no new cryptographic primitive introduced.

Consequences:
- `docs/data/event-log.md`'s `Signature` class gains the new field in
  this same pass, per this project's data-model-ownership convention.
- A new `ITimestampAuthorityClient` extension point — cataloged in
  `docs/extensibility-points.md` (core-Duplex-scoped, since both
  `ADR-066` signatures and `ADR-068` exports are core mechanisms, unlike
  `ADR-079`'s domain-scoped seam) — not yet built.
- Fully resolves `docs/10-open-questions.md` row 11 (formerly logged
  there; see `docs/changes/2026-07-31.md`) — no residual left.

**Compliance note**: RFC 3161 timestamping is well-precedented evidence
infrastructure for exactly `ADR-068`'s litigation-export use case (courts
and forensic standards routinely rely on trusted-timestamp tokens to
establish "this evidence existed, unaltered, as of this date") and
strengthens `ADR-066`'s 21 CFR Part 11 §11.50 signature-meaning capture
with an independently-verifiable timestamp, not just this system's own
`SignedAt` claim.
