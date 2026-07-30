[← Domains index](README.md)

# Domain: Industrial IoT / Predictive Maintenance

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

An industrial-asset platform where equipment sensors (vibration,
temperature, pressure, runtime counters on manufacturing/energy/plant
equipment) stream telemetry that gets correlated into derived predictive-
maintenance alerts. Reviewed as a proving-ground candidate because it is
the single best fit found for `ADR-007` (derived/materialized events —
still deferred, with no domain exercising it) and for streaming channels
at genuine sensor-fleet scale. Its weakness is the mirror image of that
strength: asset telemetry isn't personal data, so it would prove the
*non-regulated* half of this framework well but leave the *regulated*
half (masking, RBAC, erasure) largely untested.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| ISO 55000 | Asset-management system standard |
| IEC 62443 | Industrial-cybersecurity standard |

Per `proving-ground-domain.md`'s regulatory mapping, this is the
**lightest regulatory load of any candidate** reviewed — no HIPAA/GDPR/
FINRA-shaped compliance mechanism gets seriously exercised here.

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-005` — event lineage/DAG: a derived maintenance alert traces
  causally back through the raw sensor readings that produced it — a
  real DAG, not an analogy for one.
- `ADR-035` — non-authoritative capture: a raw sensor reading is
  captured immediately but shouldn't be treated as an actionable alert
  until it's been through anomaly-detection correlation/review.
- `ADR-031` — streaming channels: continuous machine telemetry at
  genuine sensor-fleet scale — the matrix bolds this as one of the
  domain's two best-fit mechanisms.
- `ADR-030` — multi-tenancy: multiple plants/operators/equipment
  manufacturers sharing the platform.
- `ADR-033`/`ADR-034` — replication/sharding: distributed sensor fleets
  across sites and regions.
- `ADR-060` — outbound webhooks: notifying downstream maintenance/ERP/
  CMMS systems of a derived alert.
- `ADR-007` (still deferred) — derived/materialized events: the matrix's
  single best fit found for this still-unbuilt mechanism — a maintenance
  alert *is* a derived/aggregated event computed over many raw sensor
  readings, not a bolted-on justification for using it.
- `ADR-070` — device input integration: direct sensor/gateway ingestion
  from plant-floor equipment.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-074` — SBOM/SOUP list: IEC 62443's industrial-cybersecurity scope
  includes component/software provenance for control-system software, a
  real if narrower fit than clinical trials' direct FDA Section 524B
  driver.
- `ADR-036` — DID/UCAN self-attestation: plausible for device
  self-attestation, not central.
- `ADR-032` — binary attachments: inspection photos, thermal-imaging
  scans, vibration-analysis reports.
- `ADR-046`/`ADR-043` (RLS) — role-based access: operators, technicians,
  and plant managers need different views, but it's supporting
  infrastructure, not the domain's defining trait.
- `ADR-058` — tenant rate limiting.
- `ADR-068` — bitemporal export/playback: reconstructing "what did we
  know about this asset's condition, and when" for a post-failure
  review.
- `ADR-061` — data residency/region-pinning.

**Weak/no fit:**
- `ADR-009`/`ADR-050`/`ADR-052` (masking + regulatory classification) —
  asset telemetry isn't personal data, so there's little to classify or
  mask.
- `ADR-043` (delegated "secondary opinion" access) — no natural analog;
  a maintenance alert doesn't need a second clinician-style reviewer.
- `ADR-045` (read access audit log) — no strong regulatory driver absent
  personal data.
- `ADR-057` (GDPR/CCPA erasure) — no personal data to erase.
- `ADR-066` (digital sign-off) — no natural sign-off workflow at the
  telemetry level.

## Special concerns

- **Proves the non-regulated half of the framework, not the regulated
  half** — per `proving-ground-domain.md`'s own framing, this domain
  would exercise lineage, streaming, and derived events well but leave
  masking, RBAC, erasure, and audit-log largely decorative rather than
  load-bearing.
- **Best fit found for `ADR-007`** — still a deferred feature with no
  domain currently exercising it; if that mechanism ever gets built, this
  is the strongest candidate to prove it against.
- **Lightest regulatory load of any candidate reviewed** — ISO 55000/
  IEC 62443 govern asset management and industrial cybersecurity, neither
  of which maps onto this framework's PII/PHI-shaped compliance
  mechanisms the way HIPAA/GDPR/FINRA do for other candidates.
- **Weak on masking/RBAC/erasure specifically because the data itself
  isn't personal** — not a gap in the mechanisms, a mismatch between this
  domain's data shape and what those mechanisms were built to protect.
- **Accessibility (`ADR-073`)** — operator/technician dashboards render
  through this framework's client the same as any other domain; WCAG
  2.1 AA applies here too, though the driver is weaker than for a
  citizen- or patient-facing domain since users are internal plant staff
  rather than the general public.

## Glossary

- **Asset Management (ISO 55000)** — The coordinated activity of an
  organization to realize value from its physical assets across their
  lifecycle, balancing cost, risk, and performance — the standard this
  domain's governing framework formalizes.
- **Condition-Based Monitoring (CBM)** — Maintenance triggered by an
  asset's actual measured condition (vibration, temperature, and
  similar) rather than a fixed calendar schedule — the immediate
  precursor practice predictive maintenance builds on; a CBM reading is
  captured the same way any raw sensor reading is here, non-authoritative
  until correlated and reviewed (`ADR-035`).
- **Digital Twin** — A virtual, continuously updated model of a physical
  asset, built from its real-time sensor data, used to simulate or
  predict its behavior without touching the physical equipment itself.
- **Historian (Data Historian)** — A purpose-built time-series database
  that plant-floor systems have traditionally used to archive
  high-frequency sensor readings — the role `ADR-031`'s streaming
  channels play in this framework.
- **Mean Time Between Failures (MTBF)** — The average operating time
  between one failure and the next for a repairable asset — a core
  reliability metric predictive-maintenance models try to extend.
- **Mean Time To Repair (MTTR)** — The average time needed to diagnose
  and fix a failed asset once it goes down — the companion metric to
  MTBF, both used to justify a predictive-maintenance program's return
  on investment.
- **Operational Technology (OT)** — The hardware and software that
  directly monitors or controls physical industrial equipment and
  processes, distinguished from conventional IT — IEC 62443's actual
  security scope.
- **Overall Equipment Effectiveness (OEE)** — A composite manufacturing
  metric (Availability × Performance × Quality) measuring how
  effectively equipment is actually used against its theoretical
  maximum — the standard KPI a derived/materialized maintenance alert
  (`ADR-007`, still deferred) would ultimately need to move.
- **Predictive Maintenance** — Maintenance timed by an actual forecast of
  impending failure, derived from sensor telemetry and models, rather
  than a fixed schedule (preventive maintenance) or a reactive
  after-the-fact repair (corrective maintenance) — this domain's own
  name, and exactly the kind of derived/materialized event `ADR-007` is
  scored against.
- **Programmable Logic Controller (PLC)** — A ruggedized industrial
  computer that directly controls machinery or processes on the plant
  floor — the typical device `ADR-070`'s sensor/gateway device input
  integration would actually be talking to.
- **SCADA (Supervisory Control and Data Acquisition)** — The software
  and hardware system industrial sites use to monitor and control
  processes across a plant or fleet in real time — historically the
  source system this domain's telemetry would be pulled from.
