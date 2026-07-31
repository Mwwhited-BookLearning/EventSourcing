[← Domains index](../README.md)

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

## Feature docs

- [Usage-Based Insurance Trip Scoring and Claim](features/usage-based-insurance-trip-scoring-and-claim.md) — driving-behavior telemetry (`ADR-031`) accumulates into a scored trip, a claim references that scored history, a delegated secondary-opinion grant (`ADR-043`) lets a second reviewer weigh in, and a disputed claim is reconstructed via lineage export and bitemporal playback (`ADR-068`).

## Glossary

- **Actuarial (Actuary / Actuarial Science)** — The discipline of
  applying statistics and probability to assess and price risk
  (mortality, morbidity, accident frequency and severity) — the
  underlying discipline behind every premium and reserve calculation in
  this domain.
- **Adjuster (Claims Adjuster)** — The person who investigates a claim
  and determines coverage and payout amount — a natural fit for
  `ADR-043`/`ADR-044`'s delegated "secondary opinion" access when a
  second adjuster or medical reviewer weighs in.
- **Claim** — A policyholder's formal request for payment under a
  policy following a covered loss — the trigger for this domain's
  claims-review workflow, and the kind of dispute `ADR-068`'s bitemporal
  export/playback reconstructs "what the carrier knew, and when" for.
- **Combined Ratio** *(synonym: composite ratio, statutory ratio — when
  applied to a company's overall results)* — An underwriting-profitability
  metric equal to the loss ratio plus the expense ratio; a result below
  100% means the underwriting book itself was profitable before
  investment income.
- **Loss Ratio** *(synonym: claims ratio — common UK usage)* — The ratio
  of claims paid (plus reserves) to premiums earned over a period — the
  headline metric a carrier uses to judge whether a line of business, or
  an individual telematics-scored policyholder segment, is priced
  correctly.
- **NAIC (National Association of Insurance Commissioners)** — The US
  standard-setting body whose model laws individual states adopt to
  regulate insurance, making US insurance regulation fragmented
  state-by-state rather than a single federal framework.
- **Policyholder** — The person or entity who owns an insurance policy
  and is the data subject for most of this domain's PHI/PII.
- **Telematics** — Data collected from a device (an OBD-II dongle, a
  smartphone app, a wearable) that measures real-world behavior —
  driving patterns, vital signs — used to inform pricing or claims,
  rather than relying on self-reported or demographic proxies alone;
  the readings `ADR-070`'s device input integration captures and
  `ADR-031`'s streaming channels typically carry.
- **Underwriting** — The process of evaluating and pricing risk before
  issuing a policy — deciding whether, and on what terms, to accept a
  given risk.
- **Usage-Based Insurance (UBI)** — An insurance pricing model that ties
  premium directly to measured behavior or usage (telematics-scored
  driving, wearable-tracked activity) rather than static demographic
  rating factors alone — this domain's own defining practice.
