[← Domains index](README.md)

# Domain: Biobanking / Biospecimen Repositories

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A biospecimen repository platform: participant-donated specimens
(blood, tissue, DNA) are collected under informed consent, stored,
derived into secondary samples (cell lines, DNA/RNA extracts, assay
results) across one or more research studies, and eventually
distributed or destroyed. Named a standout among the follow-up round of
candidates: the cleanest lineage fit found across every domain
considered (the original eight plus this later round), and the
sharpest erasure-vs-retention tension of any candidate.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| Common Rule, 45 CFR 46 (§46.116) | Informed consent for human-subjects research, including the broad-consent provision for future specimen use |
| GDPR Art. 9 | Special-category data (genetic, biometric, health) processing conditions |
| ISO 20387 | Biobanking quality-management requirements — collection, processing, storage, and distribution |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-005` — event lineage/DAG: the cleanest fit found across every
  candidate — a derived cell line or DNA extract traces back to one
  specimen as a literal DAG, not an analogy for one.
- `ADR-032` — binary attachments: specimen images, assay result files,
  and chain-of-custody documentation.
- `ADR-030` — multi-tenancy: multiple research studies/sponsors drawing
  on shared or study-specific specimen collections.
- `ADR-046`/`ADR-043` (RLS) — role-based and per-specimen row-level
  access across biobank staff, researchers, and IRB reviewers.
- `ADR-009`/`ADR-050`/`ADR-052` — masking and regulatory classification
  of participant-identifying and genetic data.
- `ADR-043`/`ADR-044` — delegated, capped access grants: a
  collaborating lab's temporary, scoped access to a specific specimen
  or derived sample is a direct fit for this mechanism.
- `ADR-045` — read access audit log over who accessed which specimen's
  record and derived data.
- `ADR-057` — GDPR erasure: a real, sharply-posed fit — see Special
  concerns below.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-035` — non-authoritative capture (an assay result pending lab
  QC review before being treated as authoritative).
- `ADR-036` — DID/UCAN self-attestation, moderate (a plausible fit for
  cross-institution researcher credentialing, not central).
- `ADR-033`/`ADR-034` — replication/sharding, moderate (multi-site
  biobank networks).
- `ADR-060` — outbound webhooks (notifying a study sponsor when a
  requested specimen or derived sample becomes available).
- `ADR-007` (still deferred) — derived/materialized events, a real but
  secondary fit (a derived sample's provenance is itself a
  lineage-DAG concept, `ADR-005`, more than an aggregation).
- `ADR-066` — digital sign-off, moderate (a lab director's sign-off on
  a derived sample's chain of custody).
- `ADR-070` — device input integration, moderate (lab instrument output
  feeding specimen assay results).
- `ADR-061` — data residency, moderate (some specimen/genetic data is
  subject to jurisdictional handling restrictions).

**Weak/no fit:**
- `ADR-031` (streaming channels) — L: specimens and derived samples are
  discrete lab facts, not a continuous telemetry stream; nothing in
  this domain's core workflow needs live streaming.
- `ADR-058` (tenant rate limiting) — L–M: biobank access is
  request-driven by researchers and staff, not a high-volume automated
  API-consumer pattern the way KYC or webhook-heavy domains are.
- `ADR-068` (bitemporal export/playback) — L–M: specimen and consent
  records change relatively infrequently compared to a domain like
  pharmacovigilance's rolling ICSR follow-up, so "as of" reconstruction
  is a real but occasional need rather than a routine one.

## Special concerns

- **Specimen-to-derived-sample lineage is literal, not analogous** — a
  DNA extract or cell line traces to the physical specimen it came from
  as an actual directed graph, making this the cleanest real-world fit
  `ADR-005`'s event lineage has been checked against of any candidate,
  original eight included.
- **The sharpest erasure-vs-retention tension of any candidate
  considered** — an irreplaceable physical specimen mid-active-study
  pulls directly against a participant's withdrawn consent under
  `ADR-057`'s erasure right; unlike some domains where this tension is
  softened by the record already being non-identifying, a biobank's
  specimen itself *is* the identifying artifact, so `erasureScope`-driven
  scoping has real, hard work to do here rather than a checkbox
  exercise.
- **Broad consent complicates the erasure question further** — the
  Common Rule's §46.116 broad-consent provision anticipates a specimen
  being used in studies not yet designed at collection time, which cuts
  against a simple "erase on request" default and argues for the same
  per-field, structural-record-preserving approach `ADR-057` already
  takes elsewhere.
- **Accessibility (`ADR-073`)** — biobank-staff and researcher-facing
  screens render through this framework's client the same as any other
  domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.
- **GDPR breach notification (Art. 33/34)** — this domain already relies
  on GDPR Art. 9 for special-category genetic/health data above; the
  72-hour notification *workflow* itself remains an open question
  (`docs/10-open-questions.md`) — `ADR-045`'s access audit log supplies
  the forensic inputs, but the notification process itself isn't
  designed yet.
