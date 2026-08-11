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

## Sample application build status

Both chosen domains now also have a real, runnable `samples/Vitals/`/
`samples/Meridian/` project set (`ADR-021`'s naming, `docs/naming.md`),
one Duplex-registration + Gherkin-driven integration-test unit per
workflow below, distinct from the design-only feature docs above (a
feature doc can be fully written with no code; this table tracks the
code). Direct request: "the proving-ground applications [should] be
built out... under the sample folder... within subfolders for each
proving-ground model," to full workflow depth. This table is the
authoritative tracker for that build, the same role `08-build-plan.md`'s
own "Implementation status" table plays for the core engine — kept
current in the same pass a workflow's own status changes, not
after-the-fact.

| Domain | Workflow | Feature doc(s) | Status |
|---|---|---|---|
| Vitals | A — Enrollment & Consent | [Patient Enrollment and Informed Consent](clinical-trials-device-telemetry/features/patient-enrollment-and-informed-consent.md) | Done |
| Vitals | B — Device Monitoring → Adverse Event Review | [Device Onboarding and Continuous Monitoring](clinical-trials-device-telemetry/features/device-onboarding-and-continuous-monitoring.md), [Adverse Event Capture and Review](clinical-trials-device-telemetry/features/adverse-event-capture-and-review.md) | Done |
| Vitals | C — Trial Data Export & Subject Rights | [Trial Data Export and Subject Rights](clinical-trials-device-telemetry/features/trial-data-export-and-subject-rights.md) | Done (erasure half only — export/playback deliberately reuses the already-proven core mechanism unchanged, see note below) |
| Vitals | D — Intraoperative Monitoring & Alert Response | [Intraoperative Monitoring and Alert Response](clinical-trials-device-telemetry/features/intraoperative-monitoring-and-alert-response.md) | Not started |
| Meridian | A — Document/Biometric Capture → Verification | [Document and Biometric Capture](digital-identity-kyc/features/document-and-biometric-capture.md), [Customer Onboarding and Identity Verification](digital-identity-kyc/features/customer-onboarding-and-identity-verification.md) | Not started |
| Meridian | B — Relying-Party Access | [Relying-Party Verification Request](digital-identity-kyc/features/relying-party-verification-request.md) | Not started |
| Meridian | C — Ongoing Screening & SAR Escalation | [Periodic Screening and SAR Escalation](digital-identity-kyc/features/periodic-screening-and-sar-escalation.md) | Not started |

**One real, load-bearing implementation note, found while scoping this
work, not while writing an individual workflow**: `RouterWorker.FoldAsync`/
`FoldLiveAsync`/`SplitByConformance` (`EventStore.Router`) — the fold
primitives a "special-purpose reactor" like `AuthorityDecisionResolver`
calls to catch the authoritative Entity Store up — are `internal`, not
`public`; a sample project in a separate assembly cannot call them
directly (confirmed: no `InternalsVisibleTo` exists anywhere in this
repo — even `EventStore.IntegrationTests` only ever drives each worker's
own `public static RunOnceAsync`, never these internal helpers). A
domain-specific decision-review reactor a sample needs (`patient-
enrollment-and-informed-consent.md`'s own "sibling `ConsentApprovalResolver`"
framing, for one) therefore cannot be a genuinely new resolver class the
way that doc's prose describes — it must instead reuse the CORE engine's
own existing, already-generic, already-tested `authoritydecision`
reserved-event-type mechanism directly (`AuthorityDecisionResolver`
already resolves purely by `targetEventId`, with zero knowledge of what
entity/event type the target actually is), the same mechanism `adverse-
event-capture-and-review.md` itself already uses under that exact
literal name. Each sample workflow below that needs a "human decides on
a captured record" step publishes under the real, reserved `authorityDecision`
type (lowercase field names `targetEventId`/`decision`/`decidingActorId`/
`reason`, exactly as the engine's own resolver expects) rather than a
domain-invented type name — a deliberate, honestly-recorded divergence
from a feature doc's own narrative choice of name, not a silent
substitution, per this repo's own "say when something is only partially
borrowed" convention.

**A second such divergence, found building Workflow B's "secondary
opinion" half**: `adverse-event-capture-and-review.md`'s own sequence
diagram shows a delegated grant read via `QUERY liveAdverseEvent(entityId)`
against a generic Live View field — no such GraphQL field exists;
"GraphQL-Only Query Layer"'s own build-scope note says explicitly "no
generic entity/`extensions: JSON` query... nothing built here ever needs
one." The only real, claims-gated, entity-scoped read this framework
actually built is `revealField` (masked-field reveal, `ADR-009`/`043`),
so `VitalsWorkflowBSecondaryOpinionHttpSqliteTests.cs` exercises that
instead — an AE's own masked `SubjectId` field, delegated via a real
`UcanDelegation` + OAuth Token Exchange round trip, the exact mechanism
`DelegatedGrantsRbacFederationHttpSqliteTests.cs` already proves for the
core engine. Also reuses the already-seeded `clinician-spa-client`/
`colleague-client` pair and their real `clearance:phi` claim rather than
seeding an unused `review:secondary-opinion` claim no client in this dev
IdP actually holds — and the feature doc's own `accessGrantRevoked`
event type has no real counterpart at all: a `UcanDelegation` is capped
only by its own TTL, with no revocation-before-expiry mechanism built
anywhere in `EventStore.Ucan`/`EventStore.Rbac` (confirmed by search, not
assumed) — a genuine, open gap, not one this sample works around.

**A third: Workflow C's export/playback half, deliberately scoped down.**
`trial-data-export-and-subject-rights.md`'s own `exportLineage`/
`playbackAsOf` sequence diagrams name two claims, `export:lineage`/
`export:playback`, that don't exist either — the real `LineageExportQueries`
gates both fields behind one ordinary GraphQL scope,
`events:lineage:read` (`GraphQlAuth.RequireScopeAsync`), the same scope
every other GraphQL field checks. `EntityErasureRequestedPayload.EntityId`
is real as `TargetEntityId`, and `EntityErasureKey.DestroyedAt`/
`KeyStoreBackendKey` are real as `ErasedAt`/`BackendName` — confirmed
against the actual classes, not assumed from the ER diagram. Since
`ADR-068`'s export/playback mechanism is already fully exercised
generically (`LineageExportHttpSqliteTests.cs`) and this domain changes
no risk in it (only which entity/event names are involved), this sample
deliberately does NOT re-prove that half with a second, redundant HTTP
test — only the erasure half (`VitalsWorkflowCScenarioAssertions.cs`),
which genuinely is domain-specific (PHI fields, a non-continuity
subject, the retention-vs-erasure tension this domain's own `README.md`
names as real), gets real sample code and tests.
