[← Domains index](../README.md)

# Domain: DSCSA Pharma Supply Chain

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A platform tracking serialized pharmaceutical units (packages, cases,
pallets) as they move through manufacturers, wholesalers, dispensers,
and repackagers, under the US Drug Supply Chain Security Act's
enhanced drug distribution security requirements. It scores high on
outbound webhooks and device input integration — trading-partner
transaction notifications and barcode/RFID scanning at each hand-off
are the domain's routine mechanics, not add-ons — as well as
replication/sharding (high volume across many independently operated
trading-partner nodes), the read access audit log, event lineage (unit
aggregation/disaggregation into cases and pallets is a literal DAG),
and multi-tenancy (each trading partner as its own tenant). Real-world
DSCSA compliance depends on GS1/EPCIS-formatted interchange with
trading partners and high-volume serialized-unit ingestion, both of
which this framework has a concrete, already-designed answer for in
`ADR-072`.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| DSCSA §582(g)(1), 21 U.S.C. §360eee-1(g)(1) | Enhanced drug distribution security — interoperable, electronic, package-level tracing, effective November 2023 |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-072` — bulk ingestion and external interchange-format adapters:
  **directly load-bearing here** — DSCSA needs outbound GS1/EPCIS-
  formatted trading-partner exchange (a concrete `Gs1EpcisAdapter`
  implementation of `IInterchangeFormatAdapter`, transforming an
  outbound event into EPCIS format ahead of `ADR-060`'s webhook
  delivery) plus the batch ingestion endpoint (`POST /publish/batch`)
  for high-volume serialized-unit exchange, where thousands of units
  move in a single trading-partner transaction.
- `ADR-060` — outbound webhooks: trading-partner transaction
  notifications (DSCSA's transaction information/history/statement,
  the "T3" documentation) are exactly this mechanism's use case.
- `ADR-070` — device input integration: barcode/RFID scanning of
  serialized units at each trade event is the domain's routine capture
  mechanism, not a special case.
- `ADR-033`/`ADR-034` — replication/sharding: DSCSA's real volume — many
  independently operated trading-partner nodes exchanging serialized-
  unit data continuously — is a genuine scale driver, not contrived.
- `ADR-045` — read access audit log: DSCSA's transaction-history
  requirement is itself an access/provenance record.
- `ADR-005` — event lineage: aggregation of units into cases and
  disaggregation of cases into units forms a literal DAG as product
  moves through the supply chain.
- `ADR-030` — multi-tenancy: each trading partner (manufacturer,
  wholesaler, dispenser, repackager) operates as its own tenant.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-032` — binary attachments: certificates of analysis, packaging
  images, moderate but real.
- `ADR-046`/`ADR-043` (RLS) — role-based access across different
  trading-partner types, moderate.
- `ADR-066` — digital sign-off: trading-partner attestations on
  transaction statements, moderate.
- `ADR-035` — non-authoritative capture: a scanned unit's data pending
  reconciliation against the manufacturer's original serialization
  record, moderate.
- `ADR-068` — bitemporal export/playback: useful for recall
  investigations and regulatory audits, moderate.
- `ADR-058` — tenant rate limiting: moderate, for high-volume
  trading-partner API traffic.
- `ADR-036` — DID/UCAN self-attestation: a plausible but secondary fit
  for trading-partner identity verification.

**Weak/no fit:**
- `ADR-031` (streaming channels) — no natural telemetry story; unit
  movement is discrete transaction events, not continuous signal data.
- `ADR-009`/`ADR-050`/`ADR-052` (masking/regulatory classification) —
  low fit; DSCSA data is overwhelmingly product/lot/transaction data,
  not personal data, so this domain would not meaningfully stress-test
  masking the way clinical trials or digital identity do.
- `ADR-057` (GDPR/CCPA erasure) — no natural driver; there is little
  personal data in a serialized-unit transaction record to erase.

## Special concerns

- **GS1/EPCIS interchange is a concrete, already-designed requirement,
  not a hypothetical** — `ADR-072`'s `IInterchangeFormatAdapter` seam
  names a `Gs1EpcisAdapter` explicitly for this domain's outbound
  trading-partner exchange; a DSCSA build would be the first real
  exercise of that adapter, composing with `ADR-060`'s webhook delivery
  as a transform step ahead of the HTTP POST rather than a replacement
  for it.
- **Bulk/batch ingestion is load-bearing, not an optimization detail**
  — a single trading-partner transaction can involve thousands of
  serialized units; `ADR-072`'s `POST /publish/batch` endpoint (NDJSON/
  JSON-array body, each event still going through `ADR-023`'s ordinary
  per-event persist-everything path) is the mechanism this domain would
  actually depend on for throughput, not a nice-to-have.
- **Low personal-data surface area is itself notable** — unlike most
  other candidates reviewed, DSCSA's data is almost entirely product and
  transaction data; a build here would exercise this framework's supply-
  chain/scale mechanisms well but would do little to stress-test the
  masking/erasure/regulated-PII half of the design.
- **DSCSA's November 2023 enhanced-security deadline is already in
  effect** — this is a live compliance requirement for the industry
  today, not a forward-looking standard, which is part of why the
  GS1/EPCIS interchange need surfaced as concretely as it did.
- **Accessibility (`ADR-073`)** — trading-partner-facing screens render
  through this framework's client the same as any other domain; WCAG
  2.1 AA applies here too, though the driver is weaker than for a
  citizen-facing domain since users are business partners rather than
  the general public.

## Feature docs

- [`features/product-serialization-and-trading-partner-transaction.md`](features/product-serialization-and-trading-partner-transaction.md) — batch unit-scan capture, a signed trading-partner transaction, and GS1/EPCIS interchange with the next trading partner (`ADR-072`, `ADR-005`, `ADR-070`, `ADR-060`, `ADR-045`).

## Glossary

- **Aggregation / Disaggregation** — combining serialized units into
  higher-order packaging (cases, pallets) or breaking them back down,
  each requiring the parent-child relationship between identifiers to
  be tracked as product moves through the supply chain — the literal
  DAG `ADR-005`'s event lineage is named against directly above.
- **Dispenser** — under DSCSA, generally a pharmacy or other person
  licensed to dispense drugs to patients; one of the four regulated
  trading-partner types, each operating as its own tenant under
  `ADR-030`.
- **DSCSA (Drug Supply Chain Security Act)** — the 2013 US federal law
  establishing enhanced, interoperable, electronic, package-level
  tracing requirements for prescription drugs, with its core
  distribution-security provisions effective since November 2023 — the
  domain's single governing standard, and what `ADR-072` was built to
  satisfy.
- **EPCIS (Electronic Product Code Information Services)** — a GS1
  standard data model and interface for capturing and sharing "what
  happened, when, where, and why" visibility events about physical
  objects; the outbound format `ADR-072`'s `Gs1EpcisAdapter` transforms
  events into ahead of `ADR-060`'s webhook delivery.
- **GS1** — the global standards organization that maintains EPCIS and
  the barcode/identifier standards (e.g., GTIN) DSCSA serialization and
  trading-partner exchange are commonly built on.
- **National Drug Code (NDC)** — the FDA's unique three-segment
  identifier (labeler, product, package size) assigned to every drug
  marketed in the US, forming part of a serialized unit's product
  identifier.
- **Repackager** — a trading partner that changes the container or
  packaging of a drug product without further manufacturing it; one of
  DSCSA's four regulated trading-partner types.
- **Serialization** — assigning a unique, machine-readable identifier
  (typically NDC plus a serial number) to each individual saleable unit
  of product — the foundational mechanism DSCSA traceability depends
  on, and the reason `ADR-072`'s batch-ingestion endpoint is
  load-bearing rather than an optimization detail.
- **Suspect / Illegitimate Product** — DSCSA's terms for, respectively,
  a product reasonably suspected of being counterfeit, diverted,
  stolen, unfit for distribution, or fraudulent (suspect), and one
  confirmed to be so after investigation (illegitimate) — triggering
  mandatory quarantine and notification; the reconciliation step
  `ADR-035`'s non-authoritative capture models is exactly where a unit
  would be flagged suspect pending investigation.
- **Track and Trace** *(synonym: traceability — used interchangeably in pharma supply-chain usage for the same forward/backward capability)* — the general industry term for the combined
  ability to follow a product forward through the supply chain (track)
  and reconstruct its path backward from any point (trace) — the
  overall capability DSCSA mandates and `ADR-005`'s event lineage
  models as a DAG.
- **Trading Partner** — any manufacturer, wholesale distributor,
  dispenser, or repackager DSCSA requires to exchange transaction data
  at each change of ownership; each operates as its own tenant under
  `ADR-030`.
- **Transaction History (TH)** — one of DSCSA's three required "T3"
  data elements: a statement documenting every prior transaction for a
  product going back to the manufacturer — exactly the notification
  `ADR-060`'s outbound webhooks are named against above.
- **Transaction Information (TI)** — one of DSCSA's three required
  "T3" data elements: the product identifiers, quantities, lot number,
  and transaction/shipment dates for a given change of ownership.
- **Transaction Statement (TS)** — one of DSCSA's three required "T3"
  data elements: a trading partner's attestation that it's authorized,
  received the product from an authorized source, and didn't knowingly
  ship suspect product or alter the transaction history — the
  trading-partner attestation `ADR-066`'s digital sign-off is named
  against above.
- **Verification Router Service (VRS)** — an industry-operated routing
  system letting a trading partner send a serialized-product
  verification request (e.g., for a saleable return or suspect-product
  investigation) to the correct manufacturer or repackager and receive
  an authoritative response — the same reconciliation round-trip
  `ADR-035`'s non-authoritative capture models pending confirmation.
