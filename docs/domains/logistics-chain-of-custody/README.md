[← Domains index](../README.md)

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
- **Accessibility (`ADR-073`)** — shipper/carrier/customs-facing
  dashboards render through this framework's client the same as any
  other domain; WCAG 2.1 AA applies here too, though the driver is
  weaker than for a citizen-facing domain since users are business
  partners rather than the general public.
- **GDPR breach notification (Art. 33/34)** — this domain already lists
  GDPR for shipping-related PII above (a small slice of its overall
  data); the 72-hour notification *workflow* itself remains an open
  question (`docs/10-open-questions.md`) — `ADR-045`'s access audit log
  supplies the forensic inputs, but the notification process itself
  isn't designed yet.

## Glossary

- **AEO (Authorized Economic Operator)** *(synonym: Trusted Trader —
  the general term multiple countries' customs agencies use for AEO and
  its national equivalents, including C-TPAT)* — the customs-to-business
  certification used outside the US (EU, Canada, Mexico, and other WCO
  members) that marks a supply-chain participant as pre-vetted and
  low-risk, entitling it to fewer border inspections; C-TPAT is the US
  equivalent program.
- **Bill of Lading (BOL)** *(synonym: B/L — the same abbreviation, a
  different formatting convention)* — a legally binding document issued
  by a carrier to a shipper that serves simultaneously as a receipt for
  goods, evidence of the transport contract, and (in negotiable form) a
  document of title — a natural candidate for `ADR-032`'s binary
  attachments.
- **Bonded Warehouse** *(synonym: Customs Warehouse — same facility,
  the term more common in EU usage)* — a customs-licensed storage
  facility where imported goods can sit without paying import
  duties/taxes until they're released for domestic sale or re-exported
  duty-free.
- **C-TPAT (Customs-Trade Partnership Against Terrorism)** — the US
  Customs and Border Protection program (the American AEO equivalent)
  that gives vetted, low-risk supply-chain partners faster, lighter-touch
  border processing.
- **Chain of Custody** — the chronological record of who held,
  controlled, or transferred a physical item and when, kept specifically
  to prove the item wasn't tampered with, substituted, or contaminated
  between handoffs — the domain's namesake concept, modeled here as a
  literal event-lineage DAG, `ADR-005`.
- **Cold Chain** — an unbroken, temperature-controlled supply chain
  (e.g., for perishables or pharmaceuticals) where a break in
  refrigeration at any handoff can spoil the goods — the in-transit
  sensor telemetry this domain's streaming-channel fit (`ADR-031`) is
  scored against.
- **Customs Bond** *(synonym: Import Bond)* — a contract of financial
  liability a bonded warehouse operator or importer posts with a
  customs agency, guaranteeing that duties will eventually be paid (or
  the goods re-exported) even though they weren't paid at the border.
- **EDI (Electronic Data Interchange)** — the decades-old standard for
  structured, computer-to-computer document exchange (purchase orders,
  shipping notices, invoices) between trading partners — the existing
  industry practice `ADR-060`'s outbound webhooks are scored as slotting
  into, not replacing.
- **Freight Forwarder** *(synonym: Forwarding Agent — used
  interchangeably in the industry)* — an intermediary that arranges
  shipment of goods on behalf of a shipper, coordinating carriers,
  customs paperwork, and warehousing without itself operating the
  vehicles or vessels.
- **In-Bond** — the status of goods moving through a country's
  transportation network, or sitting in a bonded warehouse, before
  duties have been paid — literally "under bond."
- **Incoterms** *(synonym: International Commercial Terms — literally
  what the name is short for)* — the International Chamber of
  Commerce's standardized three-letter trade terms (e.g., FOB, CIF,
  DAP) that fix exactly when risk, cost, and responsibility for goods
  pass from seller to buyer during a shipment — they determine which
  handoff in the custody chain is the legally significant one.
- **Manifest / Waybill** — a consolidated list of all cargo carried on a
  single conveyance (manifest) or a non-negotiable transport document
  for a single shipment (waybill), distinct from a bill of lading in
  that neither is a document of title.
- **Proof of Delivery (POD)** — the signature, photo, or scan captured
  at final handoff confirming a shipment reached its recipient in the
  stated condition — the ambiguous case flagged in this file's Special
  Concerns as sitting between a real digital sign-off (`ADR-066`) and
  ordinary binary-attachment capture (`ADR-032`).
- **Third-Party Logistics (3PL)** — an outsourced provider that handles
  warehousing, transportation, or fulfillment on behalf of a shipper
  that doesn't operate its own logistics network — one more tenant type
  alongside carriers and customs brokers under `ADR-030`'s
  multi-tenancy.
