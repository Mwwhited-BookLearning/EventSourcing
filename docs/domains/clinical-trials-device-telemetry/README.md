[← Domains index](../README.md)

# Domain: Clinical Trials + Connected Medical-Device Telemetry

**Status: Chosen proving-ground domain** (one of two — see
`docs/comparisons/proving-ground-domain.md` for the full comparison and
decision reasoning).

## Overview

A clinical-trials data platform where enrolled patients' connected
medical-device telemetry (vitals monitors, infusion pumps, wearables)
feeds into trial records. Chosen as a proving-ground domain because its
real-world workflow makes more of this framework's mechanisms
*load-bearing* — not merely applicable — than any other single
candidate reviewed: `ADR-043`'s delegated "secondary opinion" access was
explicitly modeled on this exact scenario before this domain was ever
named as a build target.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| FDA 21 CFR Part 11 | Electronic records/signatures — unique signer identity, record linkage, captured signature meaning |
| ICH-GCP (Good Clinical Practice) | Trial conduct, data integrity, retention |
| HIPAA | Patient health information (PHI) — Privacy/Security Rules, audit controls (§164.312(b)) |
| GDPR | EU patient data, right to erasure (Art. 17), with the Art. 17(3) exemptions this domain's own retention rules test directly |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035`/`ADR-042` — non-authoritative capture + Live View: a device
  reading or site-entered result is captured immediately but isn't
  "accepted" into the authoritative view until clinician/monitor review
  — the textbook case this mechanism was built for.
- `ADR-043`/`ADR-044` — delegated, capped, time-boxed access grants
  ("secondary opinion" access) — named after this domain specifically.
- `ADR-031` — Streaming Channels for continuous device telemetry
  (vitals waveforms, infusion-pump readings).
- `ADR-009`/`ADR-050`/`ADR-052` — PHI masking, regulatory
  classification, and streaming-channel redaction.
- `ADR-057` — GDPR erasure via crypto-shredding, directly testing the
  retention-vs-erasure tension named below.
- `ADR-066` — digital sign-off (RFC 9470 step-up auth + the `Signature`
  envelope field) satisfies 21 CFR Part 11 §11.50 directly: a clinical
  investigator's case-report-form approval is exactly this mechanism's
  target case.
- `ADR-045` — read access audit log, HIPAA §164.312(b)-shaped.
- `ADR-070` — device input integration (WebUSB/WebHID/Web Serial/Web
  Bluetooth) for connected monitoring equipment, with the native-bridge
  fallback for Safari/Firefox.
- `ADR-072` — external interchange-format adapters: **confirmed directly
  from this domain** — real integration with hospital EMR systems needs
  HL7v2 (via its real transport, MLLP/TCP, not HTTP) and/or FHIR, inbound
  into this platform's own event shape.
- `ADR-068` — bitemporal export/playback: scores H in the coverage
  matrix — "what did we know about this patient's/device's record, and
  as of when" is a routine trial-monitoring and litigation-hold need,
  not an occasional forensic exception.
- `ADR-074` — SBOM/SOUP list: **directly named by this domain, not
  hypothetical** — FDA Section 524B requires a machine-readable SBOM for
  any "cyber device" (a medical device containing software that could be
  vulnerable to cybersecurity threats) submitted via 510(k)/PMA/De Novo/
  HDE/PDP pathways, which is exactly the connected-medical-device half of
  this domain.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-005` — event lineage (a trial result derives causally from raw
  device readings — a real DAG).
- `ADR-032` — binary attachments (scanned consent forms, imaging, lab
  reports).
- `ADR-033`/`ADR-034` — multi-site replication/sharding (multi-site
  trials across hospitals/regions is the normal case).
- `ADR-030` — multi-tenancy (multiple sponsors/CROs each running
  independent studies).
- `ADR-046`/`ADR-043` (RLS) — role-based + per-patient row-level access
  (PI, coordinator, monitor, patient all need different access).
- `ADR-036` — DID/UCAN self-attestation — the domain's weakest fit
  (a plausible use for device self-attestation, not central).
- `ADR-060` — outbound webhooks (notifying a sponsor's system of trial
  events).
- `ADR-065`/`ADR-069` — local active-scope caching and pluggable
  outbox-flush triggers, for a site coordinator's device operating with
  intermittent connectivity.
- `ADR-043` (amended this session) — true-offline break-glass access
  composes directly from `ADR-036`'s device DID key: a monitoring
  device at a site with a genuine network outage can self-issue a
  capped, time-boxed emergency capability to a local operator with zero
  upstream contact, reviewed retroactively once connectivity resumes.
  This upgrades `ADR-036` from this domain's previously-listed weakest
  fit toward a real, load-bearing offline-continuity mechanism, not
  just a device-self-attestation nicety.

## Workflows

Four feature docs, three end-to-end workflows — the target structure for
this domain as a complete reference application, not four disconnected
examples. All four share the same `AppId` (`"trial1"`), the same
`EntityId` format (`{appId}:{entityType}:{uniqueId}`, `ADR-021`), and the
same continuity patient (`SubjectId` `"S-0091"`, `trial1:Patient:S-0091`)
wherever a workflow's own narrative calls for one, so a reader can follow
one patient's record across all three.

- **Workflow A — Enrollment & Consent**: a patient is screened, walked
  through informed consent, and becomes an active study participant,
  with the investigator's countersignature captured as a real `ADR-066`
  digital sign-off.
  1. [Patient Enrollment and Informed Consent](features/patient-enrollment-and-informed-consent.md)
- **Workflow B — Device Monitoring → Adverse Event Review**: a connected
  bedside monitor is paired to the patient enrolled in Workflow A, its
  continuous vitals stream is provisioned as a Streaming Channel, and a
  detector escalates a real anomaly into the adverse-event review process
  already documented for this domain.
  1. [Device Onboarding and Continuous Monitoring](features/device-onboarding-and-continuous-monitoring.md) — pairing, channel provisioning, continuous ingestion, and the detector's escalating publish.
  2. [Adverse Event Capture and Review](features/adverse-event-capture-and-review.md) — that publish's non-authoritative capture, delegated secondary-opinion review, and the investigator's signed-off decision.
- **Workflow C — Trial Data Export & Subject Rights**: two related "data
  leaving the system properly" needs — a sponsor/regulator's lineage
  export and bitemporal system-time playback of the same patient's
  record from Workflows A/B, and a GDPR erasure request for a different,
  withdrawn subject, directly stress-testing the retention-vs-erasure
  tension named below.
  1. [Trial Data Export and Subject Rights](features/trial-data-export-and-subject-rights.md)

## Special concerns

- **Retention vs. erasure, a real and useful tension, not a
  hypothetical**: ICH-GCP retention requirements often *require* long
  retention of trial records, in real tension with GDPR's right to
  erasure. `ADR-057`'s `erasureScope`-driven, per-field crypto-shredding
  (erase the *person's identifying data*, keep the *record* structurally
  intact) is directly stress-tested by this domain, not just asserted to
  work.
- **Signatory identity is categorically exempt from erasure**
  (`ADR-066`'s amendment, GDPR Art. 17(3)(b)/(e)) — an investigator's
  signature on a CRF must remain attributable forever; this domain is
  exactly why that exemption exists.
- **Device telemetry defaults to non-authoritative capture** (`ADR-035`)
  — a raw reading captured via `ADR-070`'s device input integration
  hasn't been clinically reviewed and shouldn't be treated as
  automatically trustworthy.
- **EMR/HL7 integration is real, not hypothetical**: confirmed directly
  from this domain that hospital EMR interoperability is a genuine
  requirement — `ADR-072`'s `Hl7V2Adapter` (over MLLP, HL7v2's actual
  transport) and/or `FhirAdapter` bridge hospital-sourced patient data
  into this platform's own event shape, landing as non-authoritative
  capture pending review like any other externally-sourced data.
- **SBOM/SOUP (`ADR-074`) is a direct, named requirement, not a
  cross-cutting nicety here** — connected medical devices (vitals
  monitors, infusion pumps, wearables) are exactly what FDA Section 524B
  calls a "cyber device," making this the one domain where `ADR-074`'s
  SBOM generation is a premarket-submission requirement rather than a
  general supply-chain best practice.
- **Accessibility (`ADR-073`)** — patient- and coordinator-facing
  screens (consent capture, patient-reported outcomes, site-coordinator
  review queues) render through this framework's client the same as any
  other domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.
- **GDPR breach notification (Art. 33/34)** — this domain already relies
  on GDPR for the erasure-vs-retention tension above; the 72-hour
  notification *workflow* itself remains an open question
  (`docs/10-open-questions.md`) — `ADR-045`'s access audit log supplies
  the forensic inputs, but the notification process itself isn't
  designed yet.
- **Per-modality irreversible de-identification (EEG/video) is a real,
  named gap — deliberately left to the application, not the framework.**
  Surfaced by an independent cross-reference against a separate
  architecture document ("Jason McCann's Final-State Architecture," this
  session): `ADR-009`'s masking and `ADR-052`'s streaming redaction both
  operate on *structured* content (a JSON field, a time-bounded byte
  range) — neither claims to make a raw biometric signal or a face in a
  video frame *irreversibly unidentifiable*, which is a fundamentally
  different, modality-specific problem (an EEG waveform or a
  face/gait/voice can themselves be identifying, not just the metadata
  around them). Confirmed genuinely unsolved on both sides of that
  comparison, not just this one. Correctly **not** a framework-level
  requirement — any real solution (voice/face de-identification
  algorithms, EEG feature-stripping) would be domain-specific signal
  processing, not something a generic event-sourcing engine should own —
  but a real device-telemetry deployment handling EEG/video needs its
  own answer here, layered on top of `ADR-052`'s existing redaction seam
  rather than assumed already covered by it.
- **Dual-channel live-safety vs. standard persistence is a real,
  application-specific need — already composable from `ADR-031`,
  no new mechanism.** Some device-telemetry deployments genuinely need
  a fast, low-latency review path (a bedside live waveform display,
  intentionally lower-resolution/reduced-fidelity, for immediate
  safety monitoring — e.g. catching a seizure or a desaturation event
  as it happens) alongside a separate, slower, full-fidelity path for
  the confirmed, persisted record — an informal pattern with real
  precedent in broadcast (a low-latency preview feed vs. a
  higher-fidelity master/archival encode) and in patient-monitoring
  systems generally, though not checked against a specific formal
  standard here. `ADR-031` already supports this without any change:
  a device simply publishes to **two separate `Origin` `TelemetryChannel`s**
  — one declared at a reduced sample rate/resolution for speed, one at
  full fidelity for the record — both tailed/replayed independently.
  Which channel is "the fast one" is domain/device metadata (a
  `Purpose`/label an application chooses to attach), not a framework
  concept `ADR-031` needs to formalize.

## Feature docs

All four feature docs this domain now has, grouped into the three
end-to-end workflows above (see "Workflows" for the ordering within each
one):

- [Patient Enrollment and Informed Consent](features/patient-enrollment-and-informed-consent.md) — screening, non-authoritative consent capture, and an investigator's signed-off consent countersignature (`ADR-021`, `ADR-066`, `ADR-046`, `ADR-009`). Workflow A.
- [Device Onboarding and Continuous Monitoring](features/device-onboarding-and-continuous-monitoring.md) — pairing a connected bedside monitor (`ADR-070`) and provisioning/ingesting its continuous vitals stream (`ADR-031`), ending at a detector's escalating publish. Workflow B, upstream half.
- [Adverse Event Capture and Review](features/adverse-event-capture-and-review.md) — a device reading or site-entered AE result flows from non-authoritative capture (`ADR-035`/`ADR-042`) through delegated secondary-opinion review (`ADR-043`) to an investigator's signed-off, accepted record (`ADR-066`). Workflow B, downstream half.
- [Trial Data Export and Subject Rights](features/trial-data-export-and-subject-rights.md) — sponsor/regulator lineage export and bitemporal system-time playback (`ADR-068`), and a withdrawn subject's GDPR erasure via crypto-shredding (`ADR-057`). Workflow C.

## Glossary

- **Adverse Event (AE)** — Any unfavorable or unintended sign, symptom,
  or medical condition occurring in a trial subject after starting the
  intervention, whether or not it's judged to be caused by it.
- **Case Report Form (CRF)** — The document (paper or electronic) used
  to record each trial subject's protocol-required data; an
  investigator's approval of a CRF entry is a regulated act of
  attestation — the case-report-form approval `ADR-066`'s digital
  sign-off mechanism directly targets.
- **Contract Research Organization (CRO)** — An organization a Sponsor
  contracts with to run some or all of a trial's operational conduct
  (site management, monitoring, data management) on its behalf — one of
  the tenants `ADR-030`'s multi-tenancy accounts for.
- **Good Clinical Practice (GCP)** — The ICH-published international
  ethical and scientific quality standard for designing, conducting,
  recording, and reporting trials involving human subjects.
- **Informed Consent** — The process (and signed/witnessed document) by
  which a potential trial subject is given all information needed to
  decide, voluntarily, whether to participate — a foundational GCP/Part
  11 ethical requirement, typically captured here as a scanned document
  under `ADR-032`'s binary attachments.
- **Institutional Review Board (IRB) / Ethics Committee** — An
  independent committee that reviews and approves a trial's protocol
  and consent materials to protect subjects' rights and welfare before
  and during the trial.
- **Investigator / Principal Investigator (PI)** — The person
  responsible for conducting the trial at a site and for the medical
  decisions and record approvals — including `ADR-066`'s CRF sign-off —
  made there.
- **MLLP (Minimal Lower Layer Protocol)** *(synonym: Lower Layer
  Protocol (LLP))* — The lightweight TCP-based framing protocol HL7v2
  messages are actually transmitted over in real hospital integrations,
  wrapping each message in start/end block characters — the real
  transport `ADR-072`'s `Hl7V2Adapter` targets, not HTTP.
- **Monitor (Clinical Research Associate)** — A person, typically
  employed by the Sponsor or CRO, who periodically reviews site records
  against source data to confirm subjects' rights are protected and
  data is accurate/complete — the human review step `ADR-035`/`ADR-042`
  model as the gate between captured and accepted.
- **Protocol** — The document that describes a trial's objective(s),
  design, methodology, statistical considerations, and organization —
  the authoritative rulebook a site must follow.
- **Serious Adverse Event (SAE)** — An AE meeting one of several defined
  severity criteria (results in death, is life-threatening, requires or
  prolongs hospitalization, causes persistent/significant disability, is
  a congenital anomaly, or otherwise requires intervention to prevent one
  of those outcomes) — triggers expedited regulatory reporting distinct
  from routine AE reporting.
- **Source Data Verification (SDV)** *(synonym: Source Document
  Verification)* — A monitor's or auditor's comparison of data recorded
  in a trial's CRF against the original ("source") clinical records to
  confirm accuracy — exactly the clinician/monitor review
  `ADR-035`/`ADR-042`'s non-authoritative capture and Live View are
  built around.
- **Sponsor** — The individual, company, institution, or organization
  that takes responsibility for initiating, managing, and/or financing a
  trial — one of the independent tenants `ADR-030`'s multi-tenancy
  accounts for.
