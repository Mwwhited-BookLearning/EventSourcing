[← Domains index](README.md)

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
