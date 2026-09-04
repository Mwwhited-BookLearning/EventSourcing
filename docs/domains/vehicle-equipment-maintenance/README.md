[← Domains index](../README.md)

# Domain: Vehicle/Equipment Maintenance & Fuel Logs

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A commercial fleet/heavy-equipment platform: vehicles and off-road
machinery stream telematics (position, engine hours, fuel level, fault
codes) that feed maintenance work orders coded against an industry-
standard repair taxonomy, alongside fuel-purchase logs reconciled
against that same telemetry for fuel-tax reporting. Reviewed as a
proving-ground candidate because it is a near-identical structural twin
of industrial IoT/predictive maintenance's own telemetry-to-alert bridge
(`ADR-031`/`ADR-005`), but adds two things that domain doesn't have: a
real, verified regulatory recordkeeping mandate (FMCSA) driving the
audit/retention half of this framework, and a genuine daily human
sign-off workflow (the driver vehicle inspection report) exercising
`ADR-066` in a shape neither chosen domain currently does.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| [49 CFR Part 396](https://www.ecfr.gov/current/title-49/subtitle-B/chapter-III/subchapter-B/part-396) (FMCSA, "Inspection, Repair, and Maintenance") | Requires motor carriers to systematically inspect, repair, and maintain commercial vehicles (`§396.3`); mandates a signed Driver Vehicle Inspection Report before/after each trip (`§396.11`/`§396.13`); requires annual periodic inspections (`§396.21`). Sets real, verified retention floors: general maintenance records for 1 year plus 6 months after the vehicle leaves the carrier's control (`§396.3(b)`); periodic-inspection reports for 14 months (`§396.21`); DVIRs for 3 months. |
| [International Fuel Tax Agreement (IFTA)](https://www.iftach.org/) | Interstate compact requiring carriers to keep fuel-purchase and distance records supporting their quarterly fuel-tax return for 4 years from the later of the return's due date or filing date — the direct regulatory driver for this domain's fuel-log half. |
| ISO 55000 | Asset-management system standard — applies to fleet/equipment the same general way it applies to industrial IoT's plant assets. |

Per `proving-ground-domain.md`'s regulatory-mapping methodology, this is
a genuinely **light regulatory load, but not the lightest** — unlike
industrial IoT (no comparable statute), FMCSA Part 396 and IFTA are real
federal/interstate recordkeeping mandates with specific, checkable
retention periods, giving this domain a real (if narrow) analog to the
other candidates' compliance-driven retention tensions. Neither reaches
HIPAA/GDPR/FINRA-scale breadth.

**Considered and not used**: ISO 14224 (reliability/maintenance data
classification) is scoped explicitly to the petroleum/petrochemical/
natural-gas industries — its taxonomy shape (failure modes, maintainable
items) transfers conceptually to VMRS's own hierarchical coding below,
but citing it here as a governing standard for road/off-road fleet
maintenance would be a false fit, not a verified one. SAE J1939 (the
CAN-bus wire protocol carrying the underlying engine/fuel signals this
domain's telemetry ultimately traces back to — Suspect Parameter Numbers
like Fuel Level 1, Engine Speed) is the wrong layer for this domain's own
event schema, worth exactly this one-line mention and no more; VMRS and
ISO 15143-3/AEMP 2.0 below are the standards that actually shape the
event/entity design.

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-031` — streaming channels: continuous vehicle/equipment telematics
  (position, engine hours, fuel level, fault codes) at genuine fleet
  scale, structured per ISO 15143-3/AEMP 2.0's own data model — the same
  best-fit mechanism industrial IoT uses for sensor telemetry, applied
  here to telematics.
- `ADR-005` — event lineage/DAG: a VMRS-coded maintenance work order
  traces causally back through the raw telemetry (a fault-code spike, an
  abnormal engine-hours delta) that triggered it — a real DAG, the same
  bridge shape industrial IoT's maintenance-alert-from-sensor-telemetry
  use case already proves.
- `ADR-035` — non-authoritative capture: a telematics-estimated fuel
  level or an automatically-flagged fault code isn't treated as an
  actionable maintenance/fuel-log record until reconciled against a
  driver-entered fuel receipt or confirmed by a technician.
- `ADR-066` — digital sign-off: the domain's single strongest fit found
  for this mechanism among the 13 considered-not-chosen domains — a
  Driver Vehicle Inspection Report is a real, dated, human sign-off
  required by `49 CFR 396.11`/`396.13` before a vehicle is dispatched,
  and a mechanic's own certification-of-repair signature on a defective
  DVIR is a second, distinct sign-off on the same record.
- `ADR-070` — device input integration: direct ingestion from an ELD
  (Electronic Logging Device)/telematics gateway/OBD-II adapter mounted
  on the vehicle or equipment.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-030` — multi-tenancy: multiple fleet operators/leasing companies
  sharing the platform.
- `ADR-033`/`ADR-034` — replication/sharding: distributed fleets across
  depots, states, and IFTA member jurisdictions.
- `ADR-060` — outbound webhooks: notifying a downstream fleet-maintenance
  system (a CMMS/TMS) or a fuel-tax reporting vendor of a new work order
  or reconciled fuel log.
- `ADR-032` — binary attachments: fuel receipts, repair invoices,
  damage-inspection photos.
- `ADR-068` — bitemporal export/playback: reconstructing "what did we
  know about this vehicle's maintenance/fuel history, and when" for an
  FMCSA or IFTA base-jurisdiction audit — a real, narrow analog to the
  other candidates' regulatory-retention-driven export needs.
- `ADR-045` — read access audit log: who looked at a vehicle's compliance
  record, relevant once an audit request is in progress.
- `ADR-046`/`ADR-043` (RBAC) — drivers, technicians, fleet managers, and
  auditors need different views, supporting infrastructure rather than
  this domain's defining trait.
- `ADR-058` — tenant rate limiting.
- `ADR-061` — data residency/region-pinning: cross-border fleets
  operating under both US/Canadian IFTA jurisdictions.

**Weak/no fit:**
- `ADR-009`/`ADR-050`/`ADR-052` (masking + regulatory classification) —
  vehicle/equipment telemetry isn't personal data; a driver's name/ID
  linked to a DVIR is a minor, narrow exception, not a reason to score
  this domain like a personal-data-heavy one.
- `ADR-036` (DID/UCAN self-attestation) — plausible for device
  self-attestation, not central.
- `ADR-057` (GDPR/CCPA erasure) — no meaningful personal data to erase;
  FMCSA/IFTA's own retention floors would dominate over any erasure
  request in the rare case a driver-identity field were in scope.
- `ADR-043` (delegated "secondary opinion" access) — no natural analog;
  a maintenance record doesn't need a second clinician-style reviewer
  beyond the mechanic's own certification sign-off already counted under
  `ADR-066`.
- `ADR-007` (still deferred, derived/materialized events) — a real but
  weaker fit than industrial IoT's own best-fit claim on this mechanism;
  not re-scored here to avoid diluting that domain's own standout.

## Special concerns

- **A near-structural-twin of industrial IoT/predictive maintenance, not
  an independent shape** — both domains bridge continuous telemetry into
  a lineage-linked, non-authoritative-until-reviewed alert/work-order
  event via `ADR-031`/`ADR-005`/`ADR-035`. This domain's own value as a
  *candidate* is what it adds on top: a real regulatory recordkeeping
  driver and a genuine daily sign-off workflow, not a materially
  different technical fit.
- **Real, narrow regulatory retention floors, unlike industrial IoT** —
  `49 CFR 396.3`/`396.21` and IFTA's 4-year fuel-record requirement give
  this domain an actual statute to check `ADR-068`'s export/playback and
  this framework's general "never lose or corrupt data" principle
  against, where industrial IoT has none.
- **Strongest sign-off (`ADR-066`) fit among the 13 considered-not-chosen
  domains** — the DVIR is a real, legally-required, dated human
  attestation with a second, distinct mechanic sign-off on defect
  repair, not a bolted-on justification for using the mechanism.
- **Telematics data, not clinical/financial data — light on the
  regulated half of the framework**, the same structural weakness
  industrial IoT has: masking, erasure, and delegated access stay mostly
  decorative here.
- **VMRS has no formal standards-development-organization backing** —
  it's an ATA/TMC industry convention, not an ISO/ANSI-accredited
  standard, worth stating plainly rather than implying ISO-grade
  formality it doesn't have.
- **Accessibility (`ADR-073`)** — driver/technician/fleet-manager
  dashboards render through this framework's client the same as any
  other domain; WCAG 2.1 AA applies, though the driver is weaker than for
  a citizen- or patient-facing domain since users are fleet staff rather
  than the general public.

## Feature docs

- [Telematics-Triggered Work Order and Fuel-Log Reconciliation](features/telematics-work-order-and-fuel-reconciliation.md)
  — continuous vehicle telematics (`ADR-031`) feeds a fault-detection
  process that publishes a VMRS-coded maintenance work order pointing
  back into the raw telemetry (`TelemetryPointer`) with lineage
  (`ADR-005`), gated non-authoritative until a technician's inspection
  (`ADR-035`); a driver's daily DVIR sign-off (`ADR-066`) and a
  telematics-vs-receipt fuel-log reconciliation for IFTA reporting run
  alongside it.

## Glossary

- **AEMP (Association of Equipment Management Professionals) Telematics
  Data Standard 2.0** — the JSON/XML machine-to-platform telematics data
  exchange format ISO adopted as `ISO 15143-3`; the two names refer to
  the same standard, AEMP being the industry body that originated it.
  Modeled here as `ADR-031` `TelemetrySample` batches on a `RawScalar`
  channel per data element (position, hours, fuel level).
- **Diagnostic Trouble Code (DTC)** — A standardized fault code an
  engine/vehicle control module reports when it detects an abnormal
  condition, traceable at the wire-protocol level to a SAE J1939
  Suspect Parameter Number — the typical raw signal this domain's
  fault-detection process watches for on a `TelemetryChannel`
  (`ADR-031`).
- **Driver Vehicle Inspection Report (DVIR)** — A required, dated, signed
  record of a pre- or post-trip vehicle inspection under `49 CFR
  396.11`/`396.13`, naming any defect found and, once repaired, a
  mechanic's own certification signature — modeled here as `ADR-066`'s
  digital sign-off, with the two signatures (driver, then mechanic)
  captured as distinct sign-off events on the same record.
- **Electronic Logging Device (ELD)** — A device that automatically
  records a commercial driver's hours of service and vehicle telematics,
  mandated by FMCSA for most commercial motor vehicles — the typical
  physical device this domain's `ADR-070` device-input integration
  ingests from.
- **Engine Control Module (ECM)** *(synonym: Engine Control Unit, ECU)*
  — The onboard computer that manages engine operation and reports
  diagnostic/telemetry data over the vehicle's CAN bus — the real-world
  source of the raw signal a `TelemetryChannel` (`ADR-031`) ingests.
- **International Fuel Tax Agreement (IFTA)** — An interstate/
  interprovincial compact among US states and Canadian provinces
  simplifying fuel-tax reporting for carriers operating across multiple
  jurisdictions, requiring 4 years of supporting fuel/distance records —
  the direct regulatory driver for this domain's fuel-log reconciliation
  workflow and its `ADR-068` export/playback fit.
- **International Registration Plan (IRP)** *(related, not a synonym of
  IFTA)* — A separate reciprocity agreement apportioning vehicle
  registration fees among jurisdictions based on distance traveled in
  each; commonly audited alongside IFTA using the same mileage records,
  but a distinct legal instrument governing registration, not fuel tax —
  named here only to avoid a false-synonym conflation with IFTA.
- **ISO 15143-3** — The ISO standard for telematics data exchange between
  earth-moving/construction machinery and a fleet-management platform
  (position, hours, fuel level, machine status), based on AEMP 2.0 above
  — the standard this domain's telematics `TelemetryChannel` payload
  shapes (`ADR-031`) are modeled against.
- **Suspect Parameter Number (SPN)** — A numeric identifier in the SAE
  J1939 CAN-bus protocol naming a specific measured or reported value
  (e.g. SPN 96 Fuel Level 1, SPN 190 Engine Speed) — the wire-level
  origin of the telemetry values this domain's event schema (VMRS/ISO
  15143-3) actually models; cited here only as provenance, not as a
  driving standard for the schema itself.
- **Vehicle Maintenance Reporting Standards (VMRS)** — ATA/TMC's
  hierarchical coding system for maintenance/repair work (system,
  assembly, component, complaint/cause/correction/labor codes) — an
  industry convention, not a formal SDO-backed standard, used here to
  code a `MaintenanceWorkOrder`'s own repair classification fields.
