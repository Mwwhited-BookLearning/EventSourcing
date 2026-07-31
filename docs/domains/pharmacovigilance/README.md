[← Domains index](../README.md)

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
- **Accessibility (`ADR-073`)** — safety-reviewer-facing case workflow
  screens render through this framework's client the same as any other
  domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.
- **No existing ADR designs a regulatory expedited-reporting deadline
  clock** — a genuine gap, the same shape as the already-tracked GDPR
  Art. 33/34 72-hour breach-notification gap: FDA 21 CFR 314.80/600.80
  impose a hard 15-calendar-day clock for serious, unexpected adverse
  drug reactions. `ADR-060`'s webhooks and `ADR-072`'s `IchE2bR3Adapter`
  supply the delivery mechanism once a report is ready to send, but no
  ADR designs the deadline-tracking/escalation workflow itself — a
  candidate for a future ADR, not yet decided.

## Feature docs

- [Individual Case Safety Report Intake and Signal Review](features/icsr-intake-and-signal-review.md) — ICSR capture as non-authoritative until reviewed (`ADR-035`/`ADR-042`), a reviewer's signed causality assessment (`ADR-066`), follow-up amendments via event lineage (`ADR-005`), and bitemporal system-time playback of a case's history (`ADR-068`).

## Glossary

- **Adverse Drug Reaction (ADR)** — a harmful, unintended response to a
  medicine at a normal dose. Note the acronym collision: everywhere
  else in this design package, "ADR" means Architecture Decision Record
  (as in `ADR-035`); within this domain doc's Overview and Governing
  regulations sections, "ADR" means Adverse Drug Reaction instead — the
  two are unrelated, and this entry exists to make that explicit rather
  than leaving it to context, per this project's own convention for
  disambiguating terminology collisions.
- **Causality Assessment** *(synonym: causality evaluation — used interchangeably in the pharmacovigilance literature for the same judgment)* — the structured judgment (e.g., the WHO-UMC
  scale or the Naranjo algorithm) a reviewer applies to decide how
  likely it is that a suspected drug, rather than the patient's
  underlying condition or another medicine, actually caused a reported
  reaction — the human judgment step `ADR-066`'s digital sign-off is
  meant to attribute and timestamp.
- **EudraVigilance** — the European Medicines Agency's database for
  suspected adverse drug reaction reports across the EU/EEA, used for
  signal detection and case exchange among regulators, sponsors, and
  marketing-authorization holders — the outbound destination
  `ADR-072`'s `IchE2bR3Adapter` and `ADR-060`'s webhook delivery target
  together.
- **Expedited Reporting** *(synonym: 15-Day Alert Report — FDA's own
  name, 21 CFR 314.80(c)(1)/600.80(c)(1), for this exact
  reporting obligation for serious, unexpected reactions)* — the
  regulatory requirement (FDA 21 CFR 314.80/600.80's 15-calendar-day
  clock for serious, unexpected reactions) to notify authorities of a
  safety case faster than routine periodic reporting — the
  deadline-tracking gap this file's Special concerns section already
  flags as undesigned.
- **FAERS (FDA Adverse Event Reporting System)** — the FDA's
  counterpart to EudraVigilance: the US database that receives
  adverse-event, medication-error, and product-quality-complaint
  reports supporting post-marketing drug/biologic safety surveillance.
- **ICH E2B(R3)** — the internationally harmonized electronic message
  format for exchanging individual case safety reports between sponsors
  and regulators; already named directly in this file's Governing
  regulations table and `ADR-072`'s adapter.
- **Individual Case Safety Report (ICSR)** — the structured record of
  one patient's suspected adverse reaction to one or more medicines,
  gathered from a patient, prescriber, or manufacturer report; the unit
  of intake this domain's Overview describes arriving and being amended
  over time, and the natural fit for `ADR-035`'s non-authoritative
  capture and `ADR-005`'s lineage as it's followed up.
- **MedDRA (Medical Dictionary for Regulatory Activities)** — the
  standardized, hierarchical medical terminology (System Organ Class
  down to Lowest Level Term) that ICSRs and signal-detection queries are
  coded against, maintained by the MSSO under ICH oversight; without it,
  "unusual pattern across many reports" (this file's Overview) has no
  common vocabulary to be detected in.
- **Periodic Benefit-Risk Evaluation Report (PBRER)** — the ICH E2C(R2)
  aggregate report format (superseding the older PSUR) summarizing a
  marketed drug's cumulative benefit-risk picture over a reporting
  interval, rather than any single case — the kind of aggregate output
  `ADR-007`'s still-deferred derived-event mechanism would compute.
- **Post-Marketing Surveillance** — ongoing safety monitoring of a drug
  or biologic after regulatory approval, as opposed to the controlled
  setting of a pre-approval clinical trial; the overarching activity
  this whole domain doc describes.
- **Qualified Person for Pharmacovigilance (QPPV)** — the individual an
  EU marketing-authorization holder must designate (Regulation (EC) No
  726/2004 Art. 23), resident in the EEA, personally accountable for
  that company's pharmacovigilance system — a natural signer for
  `ADR-066`'s digital sign-off on aggregate safety assessments.
- **Serious Adverse Event (SAE)** — an adverse event meeting one of ICH
  E2A's defining thresholds (death, life-threatening, hospitalization or
  its prolongation, persistent/significant disability, congenital
  anomaly, or an intervention required to prevent one of those
  outcomes) — the severity tier that triggers the 15-day expedited
  clock this file's Special concerns section notes has no owning ADR
  yet, even though `ADR-060`/`ADR-072` already supply the delivery
  mechanism once such a report is ready to send.
- **Signal (Signal Detection)** *(synonym: safety signal — used
  near-interchangeably in practice, though WHO's own definition of
  "signal" is technically the broader of the two, also covering a
  beneficial rather than only an adverse association)* — a statistically
  unusual pattern of
  adverse reactions across many ICSRs suggesting a possible causal
  association not yet confirmed — as this file's Overview states, never
  carried by any single report on its own; the concrete example named
  for `ADR-007`'s still-deferred derived-event mechanism.
