[← Domains index](README.md)

# Domain: Pharmacovigilance

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A post-market drug-safety surveillance platform: individual case safety
reports (ICSRs) on adverse drug reactions arrive from patients,
prescribers, and manufacturers; each case gets followed up and amended
over time as more information arrives; and the aggregate report
population is continuously mined for safety **signals** — a
statistically unusual pattern across many reports, not any single
report on its own. Named a standout among the follow-up round of
candidates: the best fit found for `ADR-068`'s bitemporal export/
playback and `ADR-007`'s still-deferred derived events, in both cases
because those mechanisms describe how this field's actual analytical
work already happens, not a contrived stretch to exercise them.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| FDA 21 CFR 314.80/600.80 | Post-marketing adverse drug/biologic experience reporting requirements for sponsors and manufacturers |
| EMA EudraVigilance | The EU's adverse-reaction reporting and signal-management database |
| ICH E2B(R3) | The international electronic case-safety-report (ICSR) exchange format |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035` — non-authoritative capture: an incoming adverse-event
  report is captured immediately but isn't accepted/adjudicated until a
  safety reviewer works the case — this domain's routine intake shape.
- `ADR-007` (still deferred) — derived/materialized events: signal
  detection *is* a derived event computed over many source ICSRs, the
  best fit found anywhere for this still-unbuilt mechanism.
- `ADR-068` — bitemporal export/playback: "what did we know about this
  drug's safety profile, and as of when" is this field's routine
  analytical method, not an occasional forensic reconstruction.
- `ADR-066` — digital sign-off: a safety reviewer's case adjudication or
  a qualified person's signal assessment needs the same attributable,
  step-up-authenticated signature this mechanism provides.
- `ADR-045` — read access audit log: regulator-facing accountability
  over who accessed which case report.
- `ADR-009`/`ADR-050`/`ADR-052` — masking and regulatory classification
  of patient-identifying fields carried inside an ICSR.
- `ADR-030` — multi-tenancy: multiple sponsors/manufacturers each
  running independent safety databases.
- `ADR-060` — outbound webhooks, composing with `ADR-072`'s
  `IchE2bR3Adapter` — the actual outbound reporting obligation to
  EudraVigilance/FAERS is an E2B(R3)-formatted transform ahead of
  webhook delivery, not a bespoke mechanism.
- `ADR-072` — external interchange-format adapters: confirmed as one of
  the three real examples motivating this ADR — outbound ICH E2B(R3)
  XML to EudraVigilance/FAERS is a named, direct requirement.
- `ADR-005` — event lineage: a case report's follow-up amendments and a
  detected signal both derive causally from earlier events, a real DAG.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-032` — binary attachments (lab reports, narrative case
  documents attached to an ICSR).
- `ADR-046`/`ADR-043` (RLS) — role-based access across safety reviewers,
  reporters, and regulators.
- `ADR-043`/`ADR-044` (delegated access) — a second reviewer's
  "secondary opinion" on a serious case.
- `ADR-033`/`ADR-034` — replication/sharding for a global reporting
  volume.
- `ADR-057` — GDPR erasure — real, but softer than the sharpest
  candidates, since case data is usually already substantially
  de-identified on intake.
- `ADR-058` — tenant rate limiting for high-volume automated reporters.
- `ADR-061` — data residency, moderate (some jurisdictions restrict
  where safety data may be processed).

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — L: adverse-event reporting has
  no natural self-sovereign-identity story; reporter identity is
  ordinary authenticated access, not a verifiable-credential exchange.
- `ADR-031` (streaming channels) — L–M: no continuous device-telemetry
  need at the domain's core, though a connected-device adverse event
  (an infusion-pump malfunction report) could carry a `ADR-070` device
  log as an attachment rather than a live stream.

## Special concerns

- **Bitemporal playback is this domain's routine analytical method, not
  a forensic exception** — a regulator asking "what was known about
  this drug's safety profile as of six months ago" is an ordinary
  pharmacovigilance question, not an edge case, making this the
  strongest real-world fit `ADR-068` has been checked against.
- **Signal detection is a derived-event use case for the still-deferred
  `ADR-007`** — a safety signal is computed from a pattern across many
  underlying ICSRs, not carried by any single source event; this domain
  is the clearest evidence that mechanism has a real, non-contrived
  consumer once built.
- **Outbound regulatory-format reporting is a hard requirement, not
  optional** — `ADR-072`'s `IchE2bR3Adapter` exists partly because of
  this domain: case reports must leave this platform as E2B(R3) XML to
  reach EudraVigilance/FAERS, composing with `ADR-060`'s webhook
  delivery as a transform step ahead of it rather than a separate
  mechanism.
- **Case follow-up is inherently a mutable-over-time record** — a single
  case report gets amended repeatedly as new information arrives, which
  is exactly the shape event lineage (`ADR-005`) and bitemporal playback
  (`ADR-068`) are built to represent, not a mismatch this domain has to
  work around.
