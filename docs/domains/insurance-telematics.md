[← Domains index](README.md)

# Domain: Insurance + Telematics

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

An auto or health insurance carrier's usage-based-insurance platform,
where telematics (driving-behavior sensors, health wearables) feeds
underwriting, pricing, and claims decisions. Reviewed as a proving-ground
candidate on the strength of unusually broad coverage — of the 19
mechanisms in the matrix, this domain scores H on nine and M on nine
more, with only one outright weak fit (`ADR-036`). It wasn't chosen
because neither its DID/UCAN story nor any single mechanism is as
central here as `ADR-043` is to clinical trials or `ADR-036` is to
digital identity — it's broad rather than defining.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| NAIC model laws | US state insurance regulations |
| HIPAA | Health-line policies — patient health information |
| GDPR/CCPA | EU/California policyholder data, right to erasure |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035` — non-authoritative capture: a telematics reading or
  self-reported health datum is captured immediately but shouldn't
  affect a policy or claim decision until an underwriter/adjuster review.
- `ADR-031` — streaming channels: continuous driving-behavior or wearable
  vitals telemetry.
- `ADR-032` — binary attachments: accident photos, damage assessments,
  claims documentation.
- `ADR-030` — multi-tenancy: multiple carriers/lines of business on one
  platform.
- `ADR-009`/`ADR-050`/`ADR-052` — masking + regulatory classification:
  health-line PHI and driving-behavior data both need classification and
  masking.
- `ADR-057` — GDPR/CCPA erasure: policyholder erasure requests are
  routine carrier traffic.
- `ADR-060` — outbound webhooks: notifying downstream claims/underwriting
  systems of policy or claim events.
- `ADR-068` — bitemporal export/playback: reconstructing "what did the
  carrier know, and when" for a claims dispute or regulatory inquiry.
- `ADR-070` — device input integration: direct telematics dongle/wearable
  ingestion.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-005` — event lineage: a claim decision derives from underlying
  telematics/health readings, a real but supporting DAG.
- `ADR-046`/`ADR-043` (RLS) — role-based access across underwriter,
  adjuster, and policyholder views.
- `ADR-043`/`ADR-044` — delegated/"secondary opinion" access: a second
  adjuster or medical reviewer's opinion is plausible, not the domain's
  defining trait.
- `ADR-045` — read access audit log.
- `ADR-033`/`ADR-034` — replication/sharding.
- `ADR-058` — tenant rate limiting.
- `ADR-007` (still deferred) — derived/materialized events: a risk score
  or premium adjustment computed from raw telematics is a plausible
  derived event.
- `ADR-066` — digital sign-off: policy binding or claim-approval sign-off.
- `ADR-061` — data residency/region-pinning.

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — no natural fit; a policyholder's
  identity isn't typically established via self-attested UCAN claims in
  this workflow.

## Special concerns

- **Broad but not defining** — nine H's and nine M's across the matrix
  is the widest spread of any candidate reviewed alongside clinical
  trials, but no single mechanism here is *the* reason to build against
  this domain the way `ADR-043` motivates clinical trials or `ADR-036`
  motivates digital identity.
- **Regulatory table lines up cleanly with the technical fit** — HIPAA
  (health lines) and GDPR/CCPA both map onto real H scores for masking
  and erasure here, unlike logistics or utilities where the regulatory
  table mentions GDPR/CCPA but the corresponding technical fit is weak.
- **NAIC is state-by-state, not a single federal framework** — unlike
  HIPAA or GDPR, US insurance regulation is fragmented per state, a real
  operational complexity this framework's multi-tenancy would need to
  account for if this domain were ever built.
- **No strong retention-vs-erasure tension** — unlike clinical trials,
  brokerage, or education, `ADR-057`'s footnote does not flag insurance
  as having a comparable regulatory-retention pull against erasure; it
  scores a plain H, not H*.
- **Accessibility (`ADR-073`)** — policyholder- and claimant-facing
  screens render through this framework's client the same as any other
  domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.
- **GDPR breach notification (Art. 33/34)** — this domain already relies
  on GDPR/CCPA for policyholder erasure above; the 72-hour notification
  *workflow* itself remains an open question (`docs/10-open-questions.md`)
  — `ADR-045`'s access audit log supplies the forensic inputs, but the
  notification process itself isn't designed yet.
- **No existing ADR addresses algorithmic bias/fairness auditing for
  automated underwriting or pricing decisions** — a genuine gap, not a
  stretch: several states now require it directly (e.g. Colorado
  SB21-169's testing requirement for external-data/AI models used in
  insurance, and NY DFS's AI-underwriting guidance), and telematics-driven
  pricing is exactly the kind of automated, data-driven decision those
  rules target. Nothing in this framework's ADR set designs a
  model-fairness-testing or bias-documentation mechanism — a candidate
  for a future ADR, not yet decided.
