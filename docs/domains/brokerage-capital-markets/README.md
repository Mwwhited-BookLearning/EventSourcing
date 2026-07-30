[← Domains index](../README.md)

# Domain: Brokerage / Capital Markets

**Status: Considered, not chosen** — see `docs/comparisons/proving-ground-domain.md` for the full comparison. Clinical trials + device telemetry and digital identity/KYC were chosen instead.

## Overview

A brokerage/capital-markets platform — trade capture, order/execution
recordkeeping, and account servicing for a broker-dealer. Reviewed as one
of the original eight proving-ground candidates; scored broadly strong
across this framework's regulated-domain mechanisms (RBAC/row-level
security, masking, replication/sharding, tenant rate limiting, digital
sign-off, and — its two standout mechanisms — the read access audit log
and bitemporal export/playback), but weak on the two mechanisms that made
digital identity/KYC and clinical trials the stronger picks: DID/UCAN
self-attestation and streaming channels. A follow-up review (after the
two domains above were already chosen) went back to this domain
specifically and found one genuine framework-level gap it surfaces
(`ADR-071`) and one confirming non-gap (SEC Rule 17a-4 via `ADR-019`) —
both now resolved at the framework level even though this domain itself
was never built.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| SEC Rule 17a-4 | Broker-dealer recordkeeping — WORM or, since the 2022–2023 amendment, an audit-trail alternative; already satisfied by `ADR-019`'s hash-chained Event Log, no new mechanism needed |
| FINRA | Broker-dealer conduct and recordkeeping oversight (self-regulatory organization rules) |
| MiFID II (EU) | EU markets-in-financial-instruments transaction recordkeeping/reporting |
| PCI-DSS, `ADR-071`'s boundary | Payment-card data handling if the platform is card-funded — Sensitive Authentication Data can never be registered as a schema field, full stop |
| SOX Section 404 | Internal-controls-over-financial-reporting attestation — its ITGCs are a confirmed **non-gap**, already satisfied by `ADR-045`/`ADR-019`/`ADR-067`, the same pattern as the 17a-4 finding below |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-045` — read access audit log: the strongest fit found for this
  mechanism across every candidate reviewed — FINRA/SEC audit
  requirements go beyond HIPAA's shape.
- `ADR-068` — bitemporal export/playback: this domain's other standout
  mechanism, scored alongside `ADR-045` as the strongest fit found.
- `ADR-071` — PCI-DSS Sensitive Authentication Data registration
  boundary: surfaced directly by this domain's payment-card handling,
  not a case either chosen domain exercises; hard-rejects
  `PCI-SAD`-declared schema fields at registration, not publish.
- `ADR-019` — hash-chained Event Log: confirmed, not just assumed, to
  already satisfy SEC Rule 17a-4's broker-dealer recordkeeping rule via
  its 2022–2023 audit-trail-alternative amendment — a non-gap, no new
  mechanism required if this domain is ever built.
- `ADR-005` — event lineage/DAG: real derivation chains (e.g., a
  settlement or clearing record deriving from an executed trade).
- `ADR-030` — multi-tenancy: multiple institutions/desks/accounts.
- `ADR-046`/`ADR-043` (RLS) — role-based + row-level access: different
  access for traders, compliance, back-office, and account holders.
- `ADR-009`/`ADR-050`/`ADR-052` — masking + regulatory classification:
  account/PII data alongside FINRA/SEC-classified records.
- `ADR-043`/`ADR-044` — delegated, capped access grants:
  compliance/supervisory review scenarios.
- `ADR-033`/`ADR-034` — replication/sharding: real scale and
  regional-fault-tolerance drivers.
- `ADR-060` — outbound webhooks: notifying downstream systems (clearing,
  custodians) of trade events.
- `ADR-058` — tenant rate limiting: many institutional API consumers
  against the same platform.
- `ADR-007` — derived/materialized events (still deferred): e.g. derived
  position/settlement events from raw trade events.
- `ADR-066` — digital sign-off: trade/order approvals, compliance
  attestations.
- `ADR-061` — data residency/region-pinning: cross-border trading
  (MiFID II) drives real region-pinning requirements.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-035` — non-authoritative capture: plausible (e.g. an unconfirmed
  trade report pending reconciliation) but not this domain's defining
  characteristic.
- `ADR-031` — streaming channels: scores M in the matrix (a
  market-data-feed-style telemetry story fits), though the comparison's
  own narrative calls this domain's streaming fit "weak" rather than a
  driving mechanism — treat it as the softer end of secondary fit, not
  primary.

**Weak/no fit:**
- `ADR-036` — DID/UCAN self-attestation: scores L — no natural fit for
  this domain, unlike digital identity/KYC where it's central.
- `ADR-057` — GDPR/CCPA erasure: scores L*, footnoted alongside clinical
  trials and education — FINRA/SEC/MiFID recordkeeping obligations push
  against erasure, a real retention-vs-erasure tension rather than a
  simple absence of fit.

## Special concerns

- **`ADR-071`'s PCI-SAD finding — this domain's own genuine
  framework-level gap**: brokerage's payment-card handling (e.g.
  card-funded account deposits) surfaced that PCI-DSS Requirement
  3.2/3.2.2 prohibits persisting Sensitive Authentication Data
  (CVV2/CVC2/CID, full track/magstripe data, PIN blocks) after
  authorization, under any circumstances, even encrypted — flatly
  incompatible with `ADR-023`'s persist-everything ingestion posture.
  Resolved narrowly: a reserved `x-masking.regulatoryClassification`
  value of `"PCI-SAD"` makes schema *registration* (not publish)
  hard-reject the event type outright. Full PAN itself is not SAD and
  remains covered by ordinary masking/crypto-shredding.
- **SEC Rule 17a-4 — a confirming non-gap, not a finding**: the same
  review confirmed broker-dealer recordkeeping's
  WORM-or-audit-trail-alternative requirement (2022–2023 amendment) is
  already satisfied by `ADR-019`'s existing hash-chained Event Log — no
  new mechanism needed if brokerage is ever built as a third
  proving-ground domain.
- **Retention vs. erasure tension**: footnoted in the comparison
  alongside clinical trials and education — FINRA/SEC/MiFID
  recordkeeping requirements push against GDPR/CCPA erasure. Building
  here would stress-test `ADR-057`'s `erasureScope`-driven, per-field
  crypto-shredding the same way clinical trials does.
- **Weak spots**: DID/UCAN self-attestation has no natural fit here,
  and — despite scoring M rather than L in the matrix — the
  comparison's own narrative calls streaming channels a weak point too;
  neither is this domain's defining mechanism the way they are for
  digital identity/KYC and clinical trials respectively.
- **SOX Section 404 — a confirming non-gap, same shape as the 17a-4
  finding**: its ITGC (internal-controls-over-financial-reporting)
  requirements are already satisfied by the combination of `ADR-045`'s
  read access audit log, `ADR-019`'s hash-chained tamper evidence, and
  `ADR-067`'s control-plane-actions-as-events — no new mechanism needed
  if brokerage is ever built as a third proving-ground domain.
- **Accessibility (`ADR-073`)** — trader/account-holder-facing screens
  render through this framework's client the same as any other domain;
  WCAG 2.1 AA applies here too, not just the government-case-management
  candidate it was originally tagged under.

## Glossary

- **Best Execution** — a broker-dealer's duty to execute a customer's
  order on the most favorable terms reasonably available under the
  circumstances, not merely at a legal minimum — the standard `ADR-045`'s
  read access audit log and `ADR-068`'s bitemporal export would help
  reconstruct compliance with, after the fact.
- **Broker-Dealer** — a firm registered under the Securities Exchange
  Act of 1934 to both broker trades on behalf of others and deal (trade)
  for its own account; nearly every mechanism in this domain's
  Applicable ADRs list exists to support one.
- **Clearing (Clearinghouse)** — the process, run by an intermediary
  clearinghouse, of matching, confirming, and guaranteeing a trade
  between its two counterparties before settlement — a natural
  derivation step in an event-lineage DAG (`ADR-005`) from raw trade to
  cleared trade to settled position.
- **Custodian** — an institution that holds and safeguards a client's
  securities and assets, distinct from the broker-dealer that executes
  the trade — one more actor `ADR-046`/`ADR-043`'s role-based access
  would need to distinguish.
- **FINRA (Financial Industry Regulatory Authority)** — the
  self-regulatory organization (SRO) that all US broker-dealers must
  register with alongside the SEC, and that writes and enforces
  conduct/suitability/recordkeeping rules such as Rule 2111.
- **Market Maker** — a firm that continuously quotes both a buy and sell
  price for a security, providing liquidity and profiting from the
  bid-ask spread rather than from directional bets.
- **MiFID II** — the EU's Markets in Financial Instruments Directive II,
  which imposes its own best-execution and next-business-day
  transaction-reporting obligations on EU trading activity — the
  concrete driver behind this domain's `ADR-061` data-residency/
  region-pinning fit for cross-border trading.
- **NBBO (National Best Bid and Offer)** — the single best available buy
  and sell price for a security across every US exchange at a given
  moment, computed and disseminated under Reg NMS; the reference price
  Reg NMS's Order Protection Rule (Rule 611) prohibits routing a trade
  through.
- **Reg NMS (Regulation National Market System)** — the SEC's rule set
  (adopted 2005) requiring US equity markets to protect the NBBO and
  route orders to it — the regulatory backbone best execution is
  measured against.
- **SAD (Sensitive Authentication Data)** — the PCI-DSS term for
  card-present-only secrets (CVV2/CVC2/CID, full magnetic-stripe/chip
  track data, PIN blocks) that Requirement 3.2/3.2.2 bars storing after
  authorization, even encrypted — the exact trigger for `ADR-071`'s
  registration-time hard-reject.
- **Settlement (T+1)** — the point at which securities and cash actually
  change hands after a trade; US equities moved from a two-business-day
  (T+2) to a one-business-day (T+1) settlement cycle in May 2024,
  tightening the window `ADR-019`'s hash-chained recordkeeping needs to
  keep accurate.
- **SRO (Self-Regulatory Organization)** — an industry body like FINRA,
  delegated by the SEC to write and enforce rules for its members,
  layering a second compliance regime on top of direct SEC oversight.
- **Suitability** — FINRA Rule 2111's requirement that a broker-dealer
  have a reasonable basis to believe a recommended transaction fits a
  specific customer's investment profile (age, objectives, risk
  tolerance, and similar) before recommending it.
- **WORM (Write Once, Read Many)** — the traditional storage requirement
  under SEC Rule 17a-4 for broker-dealer records, historically satisfied
  by dedicated non-erasable media; the 2022–2023 amendment added an
  audit-trail alternative that `ADR-019`'s hash-chained Event Log is
  confirmed to already satisfy.
