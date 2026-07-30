# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial. When an entry here
gets resolved, move it to a real ADR (or fold it into the ADR that
raised it) and delete the row — this file should only ever contain
things that are *actually* still open, not a permanent archive.

**Not included here, on purpose**: anything deferred purely on
scheduling with no open design question of its own — `ADR-007`
(derived/materialized event types) and `ADR-009`'s masking-enforcement
build are both fully designed, just sequenced later in
`08-build-plan.md`. Those are priority calls, not open questions — see
`CLAUDE.md`/`08-build-plan.md` for that distinction, not this file. Same
reasoning excludes a generalized-framework review's (this session)
documentation-completeness findings — missing component diagrams, the
still-partial GraphQL contract rewrite, stale `features/*.md` Gherkin
scenarios, pre-`ADR-041` DI-wiring sketches — from this table: those are
known propagation debt with no fork to weigh, already tracked in
`CLAUDE.md`'s "Genuinely still outstanding" section. That same review's
six other findings are now all resolved: client-SDK/codegen (`ADR-054`),
tenant rate limiting (`ADR-058`), data lifecycle/backup (`ADR-056`),
GDPR/CCPA erasure (`ADR-057`), and extensibility cataloging, both the
local half (`ADR-059`, `docs/extensibility-points.md`) and the outbound/
webhook half (`ADR-060`). The testing-strategy residual, the second
review's three findings, and the proving-ground-domain question are now
**all resolved too** — staged testing adoption (`ADR-063`), data
residency (`ADR-061`), package distribution (`ADR-062`), secrets
management (`ADR-041`'s addendum), and the proving-ground domain
decision (both clinical trials/device telemetry and digital identity/
KYC, `docs/comparisons/proving-ground-domain.md`). A traceability/
auditability review (this session), prompted by the two proving-ground
domains, found one clear fix (`ActorId` on every `StoredEvent`,
`ADR-064` — not tracked here, since it wasn't a real fork). The
electronic-signature fork this review also raised is now resolved too
— framework-level, via RFC 9470 step-up authentication (`ADR-066`). The
last remaining fork — control-plane audit rigor — is resolved too:
`ADR-067` models schema registration/RBAC grants/`AppTrustRoot`
registration as reserved events in the same Event Log. A follow-up
design-review pass (two independent agents, this session) then found two
more genuine forks; the first — whether a signer's identity is ever
erasable — is now resolved too: **no, categorically exempt**, per GDPR
Article 17(3)(b)/(e) (compliance with a legal obligation; establishment/
exercise/defence of legal claims) — see `ADR-066`'s amendment. Everything
else the review pass checked (ADR index accuracy, envelope-field
consistency, cross-reference correctness) came back clean or was fixed
directly as a documentation bug, not left as an open question. The
second fork — bundle-format versioning — is resolved too: matched
framework/player versions are guaranteed playable, full forward
compatibility across major versions isn't attempted, and a historical
deployment's matching player is preserved by archiving it alongside the
export rather than engineered around — see `ADR-068`'s amendment. A
compliance-focused review of the proving-ground regulatory mapping
(this session) found four more real gaps; two were resolved directly
(`ADR-039` now adopts WCAG 2.1 AA as a cross-cutting accessibility
standard; SOX Section 404's ITGCs turned out to already be satisfied by
`ADR-045`/`ADR-019`/`ADR-067`, a confirming non-gap, not a decision).
Two genuine forks remain, below.

| Question | Raised by | Why it's still open |
|---|---|---|
| Should OFAC sanctions screening and BSA Suspicious Activity Report (SAR) filing be a framework-level extensibility seam (an `ISanctionsScreeningProvider`-shaped interface, similar to `IErasureKeyStore`) or purely domain/application logic layered on top of `ADR-036`'s self-attested identity? A cryptographically valid DID/UCAN proves identity, not permissibility — screening is a separate concern this design has never addressed, real for the digital-identity/KYC proving-ground domain (an actual build target). | Proving-ground compliance review (this session) | Genuinely unweighed — no comparison has looked at whether this recurs often enough across future domains to earn a framework-level seam, or is narrow enough to leave entirely to the KYC application itself |
| What does a GDPR Art. 33/34 breach-notification workflow look like on top of `ADR-045`'s existing audit log — a formal breach register (Art. 33(5) requires logging every breach, even non-notifiable ones), and/or a 72-hour authority-notification assessment workflow? `ADR-045`/`ADR-019` already provide the forensic *inputs* (who accessed what, when, tamper-evidence), but no ADR has designed the *response* workflow itself. | Proving-ground compliance review (this session) | A real gap, not yet weighed — genuinely unclear whether this is a framework-level mechanism (a reusable breach-register data shape) or an operational/legal process this design shouldn't try to encode |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
