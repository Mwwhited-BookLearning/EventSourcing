# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

## Active

- [ ] **13 considered-not-chosen domains' feature docs use the pre-tweak
  single-screen Salt mockup.** `.claude/templates/feature-doc-
  template.md`'s Salt-mockup guidance was tightened mid-session (2026-
  07-30) from a single static mockup to a required 2–4 screen sequential
  flow, but the tweak only got applied going forward — to the 4 feature
  docs added to the two chosen domains (clinical trials, digital
  identity/KYC) afterward. The other 13 domains' single feature doc each
  (biobanking, brokerage-capital-markets, digital-forensics-evidence-
  custody, dscsa-pharma-supply-chain, education-credentials, government-
  case-management, industrial-iot-predictive-maintenance, insurance-
  telematics, itar-export-controlled-defense-data, logistics-chain-of-
  custody, pharmacovigilance, public-health-surveillance, utilities-
  smart-metering) were never revisited. Not a correctness bug — a
  structural inconsistency a reader comparing two domains' mockups would
  notice.
- [ ] **Terminology collision never actually tracked: `ChannelOrigin.Origin`
  vs. `OriginId`.** `docs/data/streaming-and-attachments.md`'s
  `TelemetryChannel.Origin` (type `ChannelOrigin`, values `Origin |
  Derived` — is this channel a raw source or computed from other
  channels, `ADR-031`) and `StoredEvent.OriginId`/`EntityStoreRow.
  LastAppliedOriginId` (which peer/site a replicated event came from,
  `ADR-033`) are unrelated concepts that both use the word "Origin."
  `CLAUDE.md`'s own "Conventions established so far" section has named
  this as a flagged-but-unfixed collision for a while, pointing at
  "Propagation status" for it — but no bullet for it ever actually
  existed there; it fell through. Either rename one of the two, or add
  an explicit disambiguation note next to both definitions (`docs/data/
  streaming-and-attachments.md` and `docs/data/event-log.md`/`entity-
  store.md`).
- [ ] **Streaming Channel, Attachment, and Live View component diagrams
  in `01-c4-architecture.md`** — not drawn.
- [ ] **`03-api-contracts.md`'s Follow/Lineage/Registry-listing sections
  still describe the OData contract in full detail**, not just a
  banner — rewriting them for the actual GraphQL contract shape is real,
  substantial work. Also doesn't mention `ADR-040`'s ticket-exchange
  endpoints, `ADR-072`'s bulk-ingestion/interchange endpoints, the
  `revealField`/export/playback/webhook-registration endpoints, or the
  RFC 9470 step-up challenge shape.
- [ ] **Every banner'd `docs/features/*.md` file's Gherkin scenarios are
  themselves still unchanged** (`400`→`202`+`SchemaStatus`, OData→GraphQL
  syntax) — the banners say what's stale, they don't fix the scenarios.
  Separately, **none** of the `docs/features/*.md` files reference any
  ADR past `ADR-053` — the entire `054`–`074` batch has zero feature-
  doc/Gherkin coverage.
- [ ] **No dedicated GraphQL-pushdown doc exists to replace
  `04-odata-filter-pushdown.md` outright** — `docs/comparisons/api-
  query-layer.md` and `docs/patterns/graphql-query-language.md` narrow
  the gap but aren't the contract-level rewrite itself.
- [ ] **`06-solution-structure.md`'s detailed DI-wiring code sketches
  predate `ADR-041`** (explicit composition) and mostly predate
  `ADR-054` onward's new projects (a webhook dispatcher, a rate limiter,
  an SDK-generation step, device-input client packages) — flagged stale
  in the file's own banner, not silently wrong, but not rewritten either.
- [ ] **`08-build-plan.md` has no phases for `ADR-050`–`ADR-079`** —
  every capability from per-tenant rate limiting through the silo
  deployment model, migration bundles, dynamic feature flags, leader
  election, and the sanctions-screening seam has no build-plan entry.
  `ADR-057` (erasure) and `ADR-062` (package distribution) most need
  real exit criteria before anything downstream is built. Candidate for
  the dependency-checklist restructuring agreed in conversation (see
  `.claude/context.md`) rather than more numbered phases tacked on the
  end.
- [ ] **`docs/features/auth.md`'s "partially superseded" banner doesn't
  yet cite `ADR-046`/`047`/`048`** (RBAC, claims augmentation, SPIFFE).
- [ ] **`docs/libraries/README.md`'s SOUP-list entries need full
  retrofitting** (known anomalies, fulfilled functional requirements per
  IEC 62304) — currently just a catalog, not yet a complete SOUP list.
- [ ] **Data-model drift table** (found by a design review, partially
  fixed — `OriginId`/`LogicalClock` closed this session, `ADR-090`):
  `RequiredClaims` still modeled
  as singular in the data model despite `ADR-050` making it a list;
  `DeprecatedAt` (`ADR-038`) and `ViewDefinition` fields (`ADR-039`)
  absent; `PeerSyncCursor`/`WebhookOutbox` tables missing; the generated-
  spec `oneOf` wrapper (`ADR-002`) still shows two branches, not three
  (`ADR-057`'s `erased`); nine entities (`LiveEntityStoreRow`,
  `EntityErasureKey`, `AppTrustRoot`, `Role`, `UserPermission`,
  `TrustedFederationIssuer`, `AppDataResidencyPolicy`,
  `WebhookSubscription`, and now also `ADR-077`'s `FeatureFlagState` and
  `ADR-078`'s `LeaderLease`, both added to `docs/data/schema-registry.md`
  this pass but not yet to this list) have no `DbSet` in
  `EventStoreContext`.
- [ ] **A `docs/naming.md`-adjacent note naming the SPDX/package-metadata
  friction as an accepted consequence of the MIT Non-AI license choice**
  — confirmed as deliberate (`docs/10-open-questions.md`, formerly row
  17), but the actual note was never written.
- [ ] **`TelemetryPointer`'s shape changed (`ADR-081`, singular object →
  list) — every existing example showing the old shape is now stale.**
  At minimum: `docs/domains/clinical-trials-device-telemetry/features/
  device-onboarding-and-continuous-monitoring.md` and `docs/domains/
  industrial-iot-predictive-maintenance/features/sensor-driven-
  maintenance-alert.md` both show a bare `"telemetryPointer": {...}`
  object in their Gherkin/JSON — needs wrapping in a one-element array
  (`"telemetryPointer": [{...}]`) to match. A mechanical sweep, not a
  design question — grep for `telemetryPointer` across `docs/`.
- [ ] **Eight domain READMEs' GDPR breach-notification notes are stale**
  — corrected count, this session (previously mis-tracked as just two):
  `docs/domains/{digital-identity-kyc,clinical-trials-device-telemetry,
  education-credentials,logistics-chain-of-custody,utilities-smart-
  metering,insurance-telematics,pharmacovigilance,biobanking}/README.md`
  all still describe the Art. 33/34 workflow as an open question; all
  need updating to reflect `ADR-045`'s addendum (resolved: deliberately
  out of framework scope) instead.
