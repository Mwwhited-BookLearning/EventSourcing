[← Domains index](../README.md)

# Domain: Public Health Surveillance / Disease Registries

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A disease-registry/notifiable-conditions surveillance platform: clinicians
and laboratories report designated conditions to public-health
authorities, whose staff investigate individual cases and aggregate them
into outbreak-level pictures across jurisdictions, forwarding
significant events onward to national and international bodies.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| HIPAA public-health exception (§164.512(b)) | Permits covered entities to disclose PHI to public-health authorities without patient authorization |
| State reportable-disease statutes | Mandate clinician/lab reporting of designated conditions to health departments |
| WHO International Health Regulations (IHR) | Cross-border notification obligations for events of international public-health concern |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035` — non-authoritative capture: a clinician- or lab-submitted
  case report is captured immediately but isn't confirmed/classified
  until a public-health investigator reviews it — this domain's normal
  intake shape.
- `ADR-030` — multi-tenancy: multiple jurisdictions/health departments
  each running independent registries, potentially sharing case data
  upward.
- `ADR-009`/`ADR-050`/`ADR-052` — masking and regulatory classification
  of patient-identifying data, load-bearing given HIPAA's public-health
  exception still requires minimum-necessary handling.
- `ADR-045` — read access audit log: accountability over who accessed a
  reportable-condition case record.
- `ADR-033`/`ADR-034` — replication/sharding: case data flowing from
  local, to state, to national/international levels is a real
  multi-region distribution shape.
- `ADR-060` — outbound webhooks, composing with `ADR-072`'s interchange
  adapters — notifying downstream public-health systems (state to CDC,
  national to WHO) of new or updated cases.
- `ADR-072` — external interchange-format adapters: outbound HL7/FHIR
  reporting to CDC-facing systems, and international case notification
  under the WHO IHR, are direct, real requirements for this domain, not
  a hypothetical extension.
- `ADR-061` — data residency/region-pinning: jurisdictional reporting
  data is often legally required to stay within its originating
  jurisdiction before aggregation upward.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-005` — event lineage (an outbreak cluster derives causally from
  individual case reports).
- `ADR-032` — binary attachments (lab confirmation results, case
  investigation documents).
- `ADR-046`/`ADR-043` (RLS) — role-based access across reporting
  clinicians, lab staff, and public-health investigators.
- `ADR-043`/`ADR-044` — delegated access (a specialist consulted on an
  unusual case).
- `ADR-057` — GDPR erasure, real but softened by the same public-health
  exception that already permits this data's collection.
- `ADR-007` (still deferred) — derived/materialized events (an
  outbreak signal aggregated across many case reports).
- `ADR-068` — bitemporal export/playback (reconstructing what was known
  about an outbreak's spread as of a given date).

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — L: case reporting runs through
  authenticated clinicians/labs and health-department staff, with no
  natural self-sovereign-identity exchange in the workflow.
- `ADR-031` (streaming channels) — L: case reports and lab results are
  discrete facts, not a continuous telemetry stream this domain's core
  workflow needs.
- `ADR-058` (tenant rate limiting) — L: reporting entities submit
  periodic, mandated reports rather than acting as high-volume
  automated API consumers.
- `ADR-070` (device input integration) — L: no direct device-integration
  story; lab-instrument results arrive as structured interchange data
  (`ADR-072`) rather than through this framework's own device-input
  mechanism.

## Special concerns

- **Outbound regulatory-format reporting is a direct, real
  requirement** — `ADR-072`'s `IInterchangeFormatAdapter` seam (an
  `Hl7V2Adapter`/`FhirAdapter` transforming an outbound event before
  `ADR-060`'s webhook delivery) exists partly to cover this domain's own
  reporting obligations upward to state/national systems and onward
  under the WHO IHR, alongside pharmacovigilance's outbound E2B(R3)
  need.
- **HIPAA's public-health exception permits collection but doesn't
  relax classification discipline** — case data disclosed to a
  public-health authority without patient authorization is still PHI,
  so `ADR-009`/`ADR-050`'s masking and classification mechanisms remain
  load-bearing rather than optional once that data is at rest in this
  platform.
- **Jurisdictional data residency is a real, not hypothetical, driver**
  — state reportable-disease statutes and cross-border WHO IHR
  notification both imply that case data's origin jurisdiction matters
  to where it may be processed, giving `ADR-061` genuine work to do here.
- **Multi-level aggregation (local → state → national → international)
  is this domain's defining data-flow shape**, distinct from the other
  two candidates in this same review round — it is fundamentally about
  case data moving *upward* through jurisdictional tiers, not a single
  organization's internal record-keeping.
- **Accessibility (`ADR-073`)** — investigator- and health-department-
  staff-facing screens render through this framework's client the same
  as any other domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.

## Feature docs

- [`features/reportable-condition-case-investigation.md`](features/reportable-condition-case-investigation.md)
  — clinician/lab case intake through investigator classification
  (`ADR-035`/`ADR-042`), an accepted case's region-pinned peer-sync
  replication (`ADR-061`) and upward HL7v2/FHIR interchange-adapter
  reporting (`ADR-072`/`ADR-060`).

## Glossary

- **Case Definition** — the standardized set of clinical, laboratory,
  and epidemiologic criteria (set by CDC/CSTE for nationally notifiable
  conditions) used to decide whether a specific patient counts as a
  case for surveillance purposes — the criteria a public-health
  investigator applies during the review this file's `ADR-035`
  non-authoritative-capture fit models: captured on report, not yet
  classified.
- **Case Investigation** — the follow-up work a public-health
  investigator does on a single reported case — verifying the
  diagnosis, gathering exposure history, and initiating contact tracing
  if warranted — the human review step that moves a captured report
  from pending to confirmed/classified under `ADR-035`.
- **Cluster** — a group of cases linked in time, place, or exposure that
  may or may not exceed the number expected, prompting further
  investigation into whether it represents a true outbreak — a
  candidate real-world instance of `ADR-007`'s still-deferred
  derived/materialized-event mechanism, aggregated from individual case
  reports.
- **Contact Tracing** — identifying and following up with people who may
  have been exposed to a person with a reportable condition, to break
  onward chains of transmission — a data-gathering workflow that extends
  a case's own event lineage (`ADR-005`) outward to the people connected
  to it.
- **Index Case** *(related, not synonymous, with the popular term
  "patient zero" — epidemiologists deliberately prefer "index case"
  (the first case to come to investigators' attention) or "primary
  case" (the actual first person infected) because "patient zero" is
  an imprecise, media-driven term that conflates the two and carries a
  history of stigmatizing misuse)* — the first identified case in a
  particular outbreak or cluster, from which contact tracing and
  cluster investigation typically start.
- **International Health Regulations (IHR)** — the WHO's binding
  framework obliging member states to notify events of international
  public-health concern across borders; already named in this file's
  regulations table as the driver behind its outbound, cross-jurisdiction
  reporting shape.
- **Minimum Necessary** *(synonym: need-to-know — many organizations
  use this informal phrase in place of the formal HIPAA term, though
  strictly need-to-know is the role-based-access mechanism that
  implements the broader minimum-necessary standard rather than a
  precise legal synonym for it)* — the HIPAA Privacy Rule's standard
  limiting the
  disclosure of PHI to the smallest amount reasonably needed for the
  stated purpose, which continues to apply even under the public-health
  disclosure exception (§164.512(b)) this file relies on — the reason
  this file's Special concerns section calls `ADR-009`/`ADR-050`'s
  masking load-bearing rather than optional.
- **NNDSS (National Notifiable Diseases Surveillance System)** — the
  CDC-run collaboration through which state, local, territorial, and
  federal public-health authorities share standardized case data on
  roughly 120 notifiable diseases and conditions — the actual
  national-level system this domain's local-to-state-to-national data
  flow (this file's Special concerns section) ultimately feeds.
- **Notifiable Disease** — a disease that a clinician or laboratory is
  legally required to report to public-health authorities on diagnosis,
  because of its contagiousness, severity, or public-health
  significance — the trigger event for this domain's entire intake
  workflow.
- **Outbreak** *(synonym: epidemic — the CDC's own epidemiology
  glossary defines "outbreak" as synonymous with "epidemic"; in
  practice "outbreak" is conventionally reached for first because it
  reads as less alarming, not because it denotes a distinct
  concept)* — an occurrence of cases of a disease clearly in excess of
  what would normally be expected in a given place or time period — the
  aggregate-level finding this domain's `ADR-007` derived-event fit and
  `ADR-068` bitemporal playback (reconstructing "what was known about an
  outbreak's spread as of a given date") both describe.
- **Public Health Authority** — a state, local, tribal, or federal agency
  (or a person/entity acting under its authority) legally responsible
  for public-health activities, the recipient HIPAA's §164.512(b)
  exception permits disclosure to without patient authorization.
- **Reportable Condition** — a disease or condition a jurisdiction's
  statute requires be reported to health authorities; not always
  identical to a nationally "notifiable" condition, since reporting
  obligations are set state by state even where CDC also tracks the same
  condition nationally.
- **Sentinel Surveillance** — monitoring a representative subset of
  healthcare facilities, providers, or regions (rather than the whole
  population) chosen because they're well-positioned to detect a
  condition of interest early or reliably — a lighter-weight complement
  to comprehensive notifiable-disease reporting.
- **Syndromic Surveillance** — monitoring symptom and clinical-sign
  patterns (ER chief complaints, OTC medication sales, absenteeism) that
  precede or substitute for a confirmed diagnosis, to detect a possible
  outbreak faster than laboratory-confirmed case reporting would allow.
