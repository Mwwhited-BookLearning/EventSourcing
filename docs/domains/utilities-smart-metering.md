[← Domains index](README.md)

# Domain: Utilities / Smart Metering

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A utility platform (electric/gas/water) ingesting continuous smart-meter
telemetry — consumption readings, grid-sensor data — for billing, load
management, and grid operations. Reviewed as a proving-ground candidate
for its genuine streaming-telemetry strength, scoring H on the same
streaming-channels row (`ADR-031`) that industrial IoT and clinical
trials also score H on, at real grid-scale continuous ingestion. Like
industrial IoT, its weakness is the regulated half of the framework:
consumption data is more personal than industrial-sensor data, but the
domain still scores weak on masking, delegated access, and erasure.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| NERC CIP | North American Electric Reliability Corporation Critical Infrastructure Protection — grid cybersecurity |
| State PUC regulations | Public Utility Commission oversight, state-by-state |
| GDPR/CCPA | Consumption data as personal data |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035` — non-authoritative capture: a raw meter reading is captured
  immediately but shouldn't be treated as authoritative for billing until
  validated (e.g., against tamper/outage conditions).
- `ADR-031` — streaming channels: continuous smart-meter and grid-sensor
  telemetry at real grid scale — the same mechanism industrial IoT and
  clinical trials also score H on.
- `ADR-030` — multi-tenancy: multiple utility operators/service
  territories on one platform.
- `ADR-033`/`ADR-034` — replication/sharding: geographically distributed
  meter fleets and grid infrastructure.
- `ADR-070` — device input integration: direct smart-meter/grid-sensor
  ingestion.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-005` — event lineage: a billing calculation or load forecast
  derives from raw meter readings, a real but supporting DAG.
- `ADR-046`/`ADR-043` (RLS) — role-based access across grid operators,
  billing staff, and customers.
- `ADR-045` — read access audit log: NERC CIP-driven accountability,
  moderate.
- `ADR-060` — outbound webhooks: outage/billing notifications to
  downstream systems.
- `ADR-058` — tenant rate limiting.
- `ADR-007` (still deferred) — derived/materialized events: a load
  forecast or anomaly alert computed from raw meter telemetry.
- `ADR-068` — bitemporal export/playback: reconstructing "what did the
  grid look like, and when" for a billing dispute or outage
  post-mortem.
- `ADR-061` — data residency/region-pinning.

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — no natural fit; meter identity
  isn't established via self-attested UCAN claims.
- `ADR-032` (binary attachments) — little natural use; meter telemetry is
  structured readings, not documents/images.
- `ADR-009`/`ADR-050`/`ADR-052` (masking + regulatory classification) —
  weak despite GDPR/CCPA appearing in the regulatory table for
  consumption data, a real tension worth naming (see below).
- `ADR-043` (delegated "secondary opinion" access) — no natural analog.
- `ADR-057` (GDPR/CCPA erasure) — weak for the same reason as masking:
  the regulatory driver exists, the technical-fit score doesn't reflect
  it strongly.
- `ADR-066` (digital sign-off) — no natural sign-off workflow at the
  meter-reading level.

## Special concerns

- **Streaming strength mirrors industrial IoT's** — both score H on
  `ADR-031` at genuine device-fleet scale, the strongest shared trait
  between the two non-regulated-leaning candidates.
- **A real, unresolved tension between the regulatory table and the
  technical-fit matrix**: the regulatory mapping explicitly lists GDPR/
  CCPA for consumption data (residential energy/water use is legally
  personal data in these frameworks), yet the masking and erasure
  mechanism scores are both L — worth flagging rather than smoothing
  over, since it means the matrix may be under-scoring how load-bearing
  those mechanisms would actually become if this domain were built out
  to serve residential customers rather than just grid operations.
- **NERC CIP is a narrower, infrastructure-specific standard** — unlike
  HIPAA/GDPR's broad personal-data scope, NERC CIP governs grid
  cybersecurity specifically, closer in spirit to industrial IoT's
  IEC 62443 than to the PHI/PII-shaped compliance regimes clinical
  trials or digital identity would exercise.
- **State PUC regulation is fragmented, state-by-state**, the same
  multi-jurisdiction operational complexity noted for insurance's NAIC
  model laws.
