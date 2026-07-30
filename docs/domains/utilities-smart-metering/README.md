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
- `ADR-074` — SBOM/SOUP list: NERC CIP-013 (supply-chain cyber security
  risk management, effective July 2020) requires grid operators to
  assess software integrity/authenticity and vendor supply-chain risk —
  a real, verified driver for `ADR-074`'s SBOM generation, alongside
  clinical trials' direct FDA Section 524B requirement.
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
- **Accessibility (`ADR-073`)** — customer- and grid-operator-facing
  screens render through this framework's client the same as any other
  domain; WCAG 2.1 AA applies here too, most directly if this domain is
  ever extended to residential-customer-facing billing/usage portals.
- **GDPR breach notification (Art. 33/34)** — this domain already lists
  GDPR/CCPA for consumption data above (the same under-scored tension
  named there); the 72-hour notification *workflow* itself remains an
  open question (`docs/10-open-questions.md`) — `ADR-045`'s access audit
  log supplies the forensic inputs, but the notification process itself
  isn't designed yet.

## Glossary

- **AMI (Advanced Metering Infrastructure)** — the two-way communication
  system (smart meters plus the network and head-end systems collecting
  from them) that lets a utility take interval reads and remotely
  control metering, replacing periodic manual meter reads — the source
  of this domain's `ADR-031` streaming-telemetry fit and `ADR-070`
  device-input-integration fit.
- **Balancing Authority** — the NERC-certified entity responsible for
  keeping electricity supply and demand balanced in real time within its
  area (dispatching generation, managing interchange with neighbors,
  holding frequency steady) — a grid-operations role distinct from an
  individual utility serving retail customers.
- **Bulk Electric System (BES)** — NERC's term for the interconnected
  transmission-level facilities (as opposed to local distribution) whose
  disruption could significantly affect grid reliability — the asset
  scope NERC CIP's cybersecurity standards actually apply to.
- **Demand Response** *(synonym: DR — the abbreviation FERC and
  industry sources commonly use)* — a change in electricity usage by
  end-use customers, away from their normal consumption pattern, in
  response to a price signal or a utility incentive payment, used to
  relieve stress on the grid at peak times — the kind of derived,
  incentive-driving signal `ADR-007`'s deferred
  derived/materialized-events mechanism would compute from raw meter
  telemetry.
- **Grid Operator** *(synonym: System Operator — used interchangeably
  for the same coordinating role; ISO/RTO are specific FERC-defined
  organizational categories of it, not separate synonyms)* — an entity
  (a balancing authority, an RTO/ISO, or a utility's own operations
  center) responsible for the real-time operation of transmission or
  distribution infrastructure, as distinct from a customer-facing
  billing or metering function.
- **Interval Data** — meter readings captured at fixed intervals
  (commonly every 15 or 60 minutes) rather than a single monthly total —
  the actual shape of the continuous smart-meter telemetry this domain
  scores H on for `ADR-031`'s streaming channels.
- **Load Forecast** *(synonym: Demand Forecast — used interchangeably
  in energy-forecasting literature)* — a prediction of future
  electricity demand derived from historical consumption and other
  signals, used for grid planning and generation dispatch — one
  concrete example of `ADR-007`'s still-deferred
  derived/materialized-events mechanism.
- **Meter Data Management System (MDMS)** — the software layer that
  validates, cleans, and stores the high-volume interval data AMI
  produces before it's used for billing or grid analytics — the kind of
  validation step this domain's `ADR-035` non-authoritative-capture fit
  (raw reading captured, not yet trusted for billing) models directly.
- **NERC CIP (Critical Infrastructure Protection)** — the mandatory,
  FERC-enforced cybersecurity and physical-security standards NERC sets
  for entities that own, operate, or use the North American Bulk
  Electric System.
- **Outage Management System (OMS)** — the utility software that tracks
  service interruptions, predicts their extent from meter and
  grid-sensor signals, and coordinates restoration — a consumer of the
  same raw telemetry this domain's streaming channels and derived-events
  mechanisms already model.
- **PUC (Public Utility Commission)** *(synonym: Public Service
  Commission (PSC) — the name some states use for the identical
  regulatory function)* — a US state-level regulatory body that
  oversees investor-owned utilities' rates, service quality, and (in
  many states) smart-meter deployment and data-privacy rules — the
  source of this domain's fragmented, state-by-state regulatory
  complexity.
- **Smart Meter** — the customer-premises device at the heart of AMI
  that records consumption at fine time resolution and communicates it
  back to the utility, replacing manual meter reading — the direct-
  ingestion device this domain's `ADR-070` device-input-integration fit
  is built around.
- **Tamper Detection** — a smart meter's or grid sensor's ability to
  flag physical interference or anomalous readings (e.g., meter bypass)
  — a concrete example of exactly the kind of "not yet trusted for
  billing" signal `ADR-035`'s non-authoritative capture is scored H
  against.
- **Time-of-Use (TOU) Rate** — an electricity pricing structure where
  the price per kWh varies by time of day (and sometimes season or day
  of week) to reflect real demand and encourage shifting usage away from
  peak periods — a billing calculation that would derive from raw
  interval data the same way a load forecast does (`ADR-007`).
