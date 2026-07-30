[← Domains index](README.md)

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
