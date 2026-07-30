[← Domains index](README.md)

# Domain: Digital Forensics / Evidence Custody

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A platform managing digital evidence through acquisition, analysis, and
litigation review — disk images, extracted files, timelines, and
examiner attestations, each requiring an unbroken, provable chain of
custody. Of all fifteen candidates this comparison considered, digital
forensics scores H or H-adjacent on more mechanisms than any other
single domain — lineage, attachments, delegated access, the audit log,
digital sign-off, bitemporal export/playback, and device input
integration all land as load-bearing here. That isn't a coincidence:
several of this session's later ADRs (`ADR-064`, `ADR-066`–`068`,
`ADR-070`) were motivated by litigation-review requirements *before*
this domain was ever named as a candidate. This is less a case of "does
this framework fit forensics" and more "this framework already fits
forensics by construction."

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| ISO/IEC 27037:2012 | Digital evidence identification, collection, acquisition, and preservation |
| US Federal Rules of Evidence 901/902 | Authentication of evidence, including self-authenticating machine-generated data |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-005` — event lineage: a derived forensic artifact (an extracted
  file, a decrypted volume, a timeline entry) traces causally to a
  source acquisition image — a literal DAG, not an analogy.
- `ADR-032` — binary attachments: disk images, extracted files, and
  other evidentiary media are attachment-shaped content, the highest
  scoring fit of any current candidate.
- `ADR-043`/`ADR-044` — delegated, capped, time-boxed access grants:
  the exact shape of granting opposing counsel or a secondary examiner
  time-boxed access to specific evidence.
- `ADR-045` — read access audit log: chain-of-custody *is* an access
  audit log — who touched this evidence, when, and under what
  credential — making this one of the cleanest fits found across every
  candidate.
- `ADR-066` — digital sign-off: examiner attestations and chain-of-
  custody sign-offs at each handoff are exactly this mechanism's target
  case.
- `ADR-068` — bitemporal export/playback: "what did this evidence show
  as of collection time, versus what it shows now" reconstructions are
  a routine litigation-review need, not an edge case.
- `ADR-070` — device input integration: forensic acquisition hardware
  (write-blockers, imaging devices, USB/serial-attached tools) maps
  directly onto this mechanism.
- `ADR-046`/`ADR-043` (RLS) — role-based + row-level access: examiners,
  case attorneys, and opposing counsel each need distinctly scoped
  access to the same evidence.
- `ADR-030` — multi-tenancy: multiple cases/matters/firms sharing the
  same platform, each strictly isolated.
- `ADR-009`/`ADR-050`/`ADR-052` — masking/regulatory classification:
  evidence routinely contains PII/PHI that must be redacted for
  discovery without altering the underlying record.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-035` — non-authoritative capture: a submitted piece of evidence
  pending forensic validation before being treated as authoritative.
- `ADR-033`/`ADR-034` — replication/sharding: moderate, for multi-site
  labs or distributed case teams.
- `ADR-057` — GDPR/CCPA erasure: moderate; evidence retention
  obligations create a tension similar to clinical trials', though less
  central to the domain's identity.
- `ADR-060` — outbound webhooks: moderate, for case-management-system
  notifications.
- `ADR-007` — derived/materialized events: moderate, for timeline or
  pattern-of-life aggregation across many source artifacts.

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — no natural self-sovereign
  identity story; examiner identity is credentialed institutionally, not
  self-attested.
- `ADR-031` (streaming channels) — low-to-moderate at best; occasional
  fit for continuous acquisition logging, not a defining characteristic.
- `ADR-058` (tenant rate limiting) — no strong driver; this isn't a
  high-volume public-facing API domain.

## Special concerns

- **Most of the relevant mechanisms were built for this domain's use
  case before the domain itself was named** — `ADR-064`, `ADR-066`
  through `ADR-068`, and `ADR-070` were all motivated by litigation-
  review requirements during this session, well before digital forensics
  was considered as a candidate. The question this domain answers isn't
  "does this framework fit forensics" so much as "this framework already
  fits forensics by construction" — a rare case where the proving-ground
  review confirmed fit after the fact rather than discovering a gap.
- **FRE 901/902 self-authentication maps directly onto existing
  primitives** — the rule's allowance for self-authenticating
  machine-generated data lines up with `ADR-019`'s hash-chained log
  combined with `ADR-066`'s digital sign-off, rather than requiring any
  new authentication mechanism.
- **ISO/IEC 27037's chain-of-custody requirement is this framework's
  read access audit log, not a bespoke addition** — `ADR-045`'s
  `AccessLogEntry`, hash-chained independently of the event log itself,
  is a direct structural match for what evidence-handling standards
  already require documented.
- **Delegated access here is adversarial in a way clinical trials'
  "secondary opinion" access isn't** — granting opposing counsel access
  to evidence is a genuinely different trust posture than a clinician
  seeking a colleague's opinion; both use `ADR-043`'s same capped,
  entity-scoped grant mechanism, but the review should note the
  difference in intent rather than assume the identical mechanism means
  an identical use case.
