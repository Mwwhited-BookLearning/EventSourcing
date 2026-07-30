[← Domains index](README.md)

# Domain: Logistics / Chain of Custody

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A shipment/freight-tracking platform where custody of goods passes
through multiple handlers (origin, carriers, customs, warehouses,
destination), each handoff a real event in a chain-of-custody history.
Reviewed as a proving-ground candidate for the cleanest, most natural fit
found for event lineage of any candidate — a shipment's custody chain
*is* a DAG, not an analogy for one — and for outbound webhooks, since
partner-integration notifications are already how this industry's
EDI-style systems work today. Weak on masking/erasure, similar to
industrial IoT: custody-chain metadata isn't primarily personal data.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| C-TPAT/AEO | Customs-Trade Partnership Against Terrorism / Authorized Economic Operator — customs security programs |
| GDPR | Shipping-related PII (sender/recipient personal data) |
| Country-specific export/trade regulations | Cross-border shipment compliance |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-005` — event lineage/DAG: the cleanest, most natural fit found
  across every candidate reviewed — a shipment's custody chain literally
  *is* a DAG of handoffs, not a mechanism bolted on to justify using it.
- `ADR-032` — binary attachments: bills of lading, customs
  documentation, proof-of-delivery photos/signatures.
- `ADR-030` — multi-tenancy: multiple shippers/carriers/logistics
  providers on one platform.
- `ADR-033`/`ADR-034` — replication/sharding: geographically distributed
  custody handoffs across sites and regions.
- `ADR-060` — outbound webhooks: the matrix bolds this as the domain's
  single best fit — partner-integration notifications on custody events
  are already how this industry's EDI-style systems operate today.
- `ADR-058` — tenant rate limiting: high-volume partner/carrier
  integrations.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-035` — non-authoritative capture: a scanned handoff event pending
  reconciliation, plausible but not the domain's central mechanism.
- `ADR-031` — streaming channels: in-transit sensor telemetry (cold-chain
  temperature, GPS location), real but secondary to the custody-chain
  events themselves.
- `ADR-046`/`ADR-043` (RLS) — role-based access across shippers,
  carriers, and customs.
- `ADR-045` — read access audit log: customs/compliance-driven
  accountability, moderate.
- `ADR-007` (still deferred) — derived/materialized events: an
  estimated-arrival or exception alert computed from custody events.
- `ADR-068` — bitemporal export/playback: reconstructing custody state at
  a point in time for a dispute or customs inquiry.
- `ADR-070` — device input integration: scanners, RFID/barcode readers,
  IoT sensors at handoff points.
- `ADR-061` — data residency/region-pinning: cross-border shipments touch
  country-specific export/trade regulation.

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — no natural fit; custody
  handoffs aren't verified via self-attested identity claims.
- `ADR-009`/`ADR-050`/`ADR-052` (masking + regulatory classification) —
  custody-chain metadata isn't primarily personal data, so there's little
  to classify or mask.
- `ADR-043` (delegated "secondary opinion" access) — no natural analog.
- `ADR-057` (GDPR/CCPA erasure) — the shipping-PII GDPR entry in the
  regulatory table is real, but it's a small slice of the overall data
  (sender/recipient identity), not the bulk of what the platform tracks —
  hence the weak technical-fit score despite GDPR appearing in the
  regulatory mapping.
- `ADR-066` (digital sign-off) — rated L–M: a proof-of-delivery signature
  is plausible but not the strong step-up-auth-shaped sign-off workflow
  this mechanism targets.

## Special concerns

- **Cleanest lineage fit of any candidate reviewed** — a literal DAG, not
  an analogy, matching the same story biobanking later proved even more
  sharply (a derived cell line tracing to one specimen) — logistics was
  the original domain that established this pattern.
- **Webhooks are already how the industry works, not a new integration
  style being introduced** — EDI-style partner notification is existing
  practice this mechanism would slot into, not invent.
- **Weak on masking/erasure, same shape as industrial IoT** — the
  regulatory table does list GDPR here (shipping PII), but that PII is
  incidental to the domain's core data (custody-chain state), unlike
  insurance or clinical trials where personal/health data is the bulk of
  what's tracked.
- **Digital sign-off's split L–M rating** is the only mechanism in this
  matrix scored as a range rather than a single letter for this domain —
  worth a second look if this domain were ever actually built, since a
  proof-of-delivery signature sits ambiguously between "real sign-off"
  and "not this mechanism's target case."
