# Proving-Ground Domain Reference

A **sixth kind of document**, alongside `docs/adrs/` (a decision),
`docs/patterns/` (a general pattern explained), `docs/comparisons/`
(a fork weighed in full), `docs/libraries/` (an adopted library), and
`references.md` (a bibliography): one file per real-world domain
considered as a proving-ground worked example for this framework —
which ADRs apply and why, which regulations/standards actually govern
it, and any special concerns (a real tension, a weak spot, a standout
fit) worth knowing before building against it. Every file now also ends
with its own `## Glossary` section — that industry's own jargon (a Case
Report Form, a Beneficial Owner, an EPCIS event), verified before
writing rather than recalled from memory. Distinct from
[`docs/glossary.md`](../glossary.md), which covers Duplex's own
cross-cutting engine terms once, not per domain.

Not a duplicate of `docs/comparisons/proving-ground-domain.md` — that
comparison doc is where the *choice* was made (the full H/M/L coverage
matrix, the regulatory mapping table, the reasoning behind picking two
domains over one). These files are the per-domain reference a reader
lands on afterward: "I'm building the clinical-trials example — which
ADRs actually matter here, and what do I need to know about 21 CFR Part
11." Each file is generated from — not a repeat of — that comparison's
matrix and regulatory table, reorganized per-domain instead of
per-mechanism.

## Catalog

| Domain | Status |
|---|---|
| [Clinical trials + connected medical-device telemetry](clinical-trials-device-telemetry.md) | **Chosen proving-ground domain** |
| [Digital identity / KYC](digital-identity-kyc.md) | **Chosen proving-ground domain** |
| [Industrial IoT / predictive maintenance](industrial-iot-predictive-maintenance.md) | Considered, not chosen |
| [Insurance + telematics](insurance-telematics.md) | Considered, not chosen |
| [Logistics / chain-of-custody](logistics-chain-of-custody.md) | Considered, not chosen |
| [Brokerage / capital markets](brokerage-capital-markets.md) | Considered, not chosen — surfaced `ADR-071` |
| [Education / credentials](education-credentials.md) | Considered, not chosen |
| [Utilities / smart metering](utilities-smart-metering.md) | Considered, not chosen |
| [Pharmacovigilance](pharmacovigilance.md) | Considered, not chosen — strongest bitemporal-playback fit found |
| [Biobanking / biospecimen repositories](biobanking.md) | Considered, not chosen — strongest lineage fit found |
| [Public health surveillance / disease registries](public-health-surveillance.md) | Considered, not chosen |
| [ITAR/export-controlled defense data](itar-export-controlled-defense-data.md) | Considered, not chosen — first domain making region-pinning load-bearing |
| [Government case management](government-case-management.md) | Considered, not chosen |
| [Digital forensics / evidence custody](digital-forensics-evidence-custody.md) | Considered, not chosen — near-total coverage, several ADRs already shaped around it unnamed |
| [DSCSA pharma supply chain](dscsa-pharma-supply-chain.md) | Considered, not chosen |

See [`docs/comparisons/proving-ground-domain.md`](../comparisons/proving-ground-domain.md)
for the full coverage matrix, regulatory mapping table, and the decision
reasoning behind choosing the first two over the other thirteen.
