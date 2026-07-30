# Proving-Ground Domain Reference

A **sixth kind of document**, alongside `docs/adrs/` (a decision),
`docs/patterns/` (a general pattern explained), `docs/comparisons/`
(a fork weighed in full), `docs/libraries/` (an adopted library), and
`references.md` (a bibliography): one folder per real-world domain
considered as a proving-ground worked example for this framework —
which ADRs apply and why, which regulations/standards actually govern
it, and any special concerns (a real tension, a weak spot, a standout
fit) worth knowing before building against it. Every domain's
`README.md` ends with its own `## Glossary` section — that industry's
own jargon (a Case Report Form, a Beneficial Owner, an EPCIS event),
verified before writing rather than recalled from memory. Distinct from
[`docs/glossary.md`](../glossary.md), which covers Duplex's own
cross-cutting engine terms once, not per domain.

Not a duplicate of `docs/comparisons/proving-ground-domain.md` — that
comparison doc is where the *choice* was made (the full H/M/L coverage
matrix, the regulatory mapping table, the reasoning behind picking two
domains over one). Each domain's `README.md` is the per-domain reference
a reader lands on afterward: "I'm building the clinical-trials example —
which ADRs actually matter here, and what do I need to know about 21 CFR
Part 11." Generated from — not a repeat of — that comparison's matrix
and regulatory table, reorganized per-domain instead of per-mechanism.

**Restructured into subfolders this session**, per direct request —
each domain is now a folder, not a single file, since a domain needs
more than a reference doc to be useful as a worked example:

```
docs/domains/{domain-slug}/
  README.md            -- the reference doc described above (ADRs, regulations, special concerns, glossary)
  features/
    {use-case}.md       -- entity/event structures, a state-machine workflow diagram,
                         -- a Salt screen mock, and embedded Gherkin -- one real use case
                         -- worked all the way through, the same depth as docs/features/*.md
                         -- for the framework's own core mechanisms, applied to this domain
```

## Catalog

| Domain | Status |
|---|---|
| [Clinical trials + connected medical-device telemetry](clinical-trials-device-telemetry/README.md) | **Chosen proving-ground domain** |
| [Digital identity / KYC](digital-identity-kyc/README.md) | **Chosen proving-ground domain** |
| [Industrial IoT / predictive maintenance](industrial-iot-predictive-maintenance/README.md) | Considered, not chosen |
| [Insurance + telematics](insurance-telematics/README.md) | Considered, not chosen |
| [Logistics / chain-of-custody](logistics-chain-of-custody/README.md) | Considered, not chosen |
| [Brokerage / capital markets](brokerage-capital-markets/README.md) | Considered, not chosen — surfaced `ADR-071` |
| [Education / credentials](education-credentials/README.md) | Considered, not chosen |
| [Utilities / smart metering](utilities-smart-metering/README.md) | Considered, not chosen |
| [Pharmacovigilance](pharmacovigilance/README.md) | Considered, not chosen — strongest bitemporal-playback fit found |
| [Biobanking / biospecimen repositories](biobanking/README.md) | Considered, not chosen — strongest lineage fit found |
| [Public health surveillance / disease registries](public-health-surveillance/README.md) | Considered, not chosen |
| [ITAR/export-controlled defense data](itar-export-controlled-defense-data/README.md) | Considered, not chosen — first domain making region-pinning load-bearing |
| [Government case management](government-case-management/README.md) | Considered, not chosen |
| [Digital forensics / evidence custody](digital-forensics-evidence-custody/README.md) | Considered, not chosen — near-total coverage, several ADRs already shaped around it unnamed |
| [DSCSA pharma supply chain](dscsa-pharma-supply-chain/README.md) | Considered, not chosen |

See [`docs/comparisons/proving-ground-domain.md`](../comparisons/proving-ground-domain.md)
for the full coverage matrix, regulatory mapping table, and the decision
reasoning behind choosing the first two over the other thirteen.
