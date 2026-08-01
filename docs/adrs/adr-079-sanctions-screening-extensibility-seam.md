[← ADR index](../07-adrs.md)

# ADR-079: Pluggable sanctions/watchlist screening — an application-scoped extension point, not core Duplex

Status: Accepted

Context: `docs/10-open-questions.md` asked whether OFAC sanctions
screening and BSA Suspicious Activity Report (SAR) filing should be a
framework-level extensibility seam (an `ISanctionsScreeningProvider`-
shaped interface, similar to `ADR-057`'s `IErasureKeyStore`) or purely
domain/application logic layered on top of `ADR-036`'s self-attested
identity. A cryptographically valid DID/UCAN proves identity, not
permissibility — screening is a separate concern this design had never
addressed, real for the digital-identity/KYC (Meridian) proving-ground
domain. `docs/domains/digital-identity-kyc/features/periodic-screening-
and-sar-escalation.md` already demonstrates the purely-manual version of
this (a compliance officer reviewing a match by hand); this ADR decides
whether an automated screening call is worth a named, reusable contract.

Decision:
- **Yes, an extensibility seam** — shaped exactly like `ADR-057`'s
  `IErasureKeyStore`: an interface (e.g. `ISanctionsScreeningProvider`,
  a `ScreenAsync(IdentityClaim claim) -> ScreeningResult`-shaped method),
  keyed-DI, multiple backends registrable and selectable per `AppId`/
  entity via configuration — not a single global choice, the same
  Strategy-pattern shape `docs/extensibility-points.md` already
  documents for every other seam in this design.
- **Scoped to the KYC/Meridian application's own composition root, not
  core Duplex.** Unlike `IErasureKeyStore` — genuinely universal, since
  every deployment needs GDPR/CCPA erasure regardless of domain —
  OFAC/BSA screening is AML/KYC-specific: clinical trials and most other
  domains have no use for it at all. `ADR-059` already establishes the
  general pattern this relies on without needing any change: "a hosting
  team's custom implementation registered the same way in *their own*
  composition root" already covers a hosting team defining and
  registering an interface for *their own* need, not only plugging into
  interfaces the framework predefines. No new interface ships inside
  core Duplex's own package as a result of this ADR.
- **This is the first domain-scoped (non-core) extension point in this
  design — worth naming as a precedent explicitly**, rather than leaving
  it ambiguous whether every extension point must be promoted into core
  Duplex to count as "real." Cataloged in `docs/domains/digital-
  identity-kyc/README.md`'s Applicable ADRs section, **not**
  `docs/extensibility-points.md` (which `docs/extensibility-points.md`'s
  own intro scopes to "this framework," i.e. core Duplex).
- **Invocation point: an automated detector's publish, gated exactly
  like any other non-authoritative capture** (`ADR-035`/`ADR-042`) — a
  sanctions-list hit lands `unattested`/`pending_review`, same as the
  worked example's manual match already models, not a new or separate
  review pipeline. The provider supplies a *signal*; a compliance
  officer's `authorityDecision` (`ADR-046`-gated) remains the actual
  decision, unchanged from the existing feature doc.

Consequences:
- `docs/domains/digital-identity-kyc/README.md`'s Special Concerns note
  ("no existing ADR addresses OFAC sanctions screening... a candidate
  for a future ADR") is superseded by this ADR — propagation work to
  update that file, not yet done.
- Establishes that not every extension point needs to be promoted into
  core Duplex to be a real, documented, reusable seam — a precedent
  future domain-specific needs (e.g. a similar automated-screening shape
  in a different regulated domain) can point back to, rather than each
  independently re-litigating "does this belong in core."
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 1).
