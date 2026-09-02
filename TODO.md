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

Every item previously tracked here (Naive UI/Vue Router shell,
`style-guide.md`, playbook diagrams/restructure/new playbooks/READMEs,
paged entity-list data grids, configurable-presentation-type charting,
JSON Schema field/dependent-field validation, calculated fields, the
PlantUML `.puml`/Docker-render migration) is done, per the workflow
above: deleted from this file, full narrative in
[`docs/changes/2026-08-28.md`](docs/changes/2026-08-28.md) and
[`docs/changes/2026-08-29.md`](docs/changes/2026-08-29.md).

(The "DSL for user flows/validations/approvals" ask was moved to
[`docs/10-open-questions.md`](docs/10-open-questions.md) row 1, not kept
here — a genuinely undecided fork, not decided work with only the doing
left.)

- [ ] **A generic demo identity still can't publish a real Vitals/Meridian
  business event over HTTP** — the narrower, still-genuinely-open half of
  the "Dispatch a command" demo-panel gap (`docs/changes/2026-09-02.md`
  closed the OTHER half, the field-casing gap, for real). No DevIdp-seeded
  HTTP client anywhere holds the specific `RequiredClaims` any real
  business event type demands (e.g. `PatientScreened`'s `patient:enroll`,
  `VitalsWorkflowA.cs`) — those events have only ever been created
  in-process by `Samples.Vitals.Seed`/`Simulator` calling `PublishService`
  directly, bypassing the HTTP auth layer's claim check entirely. This is
  a real security-policy decision, not a technical gap: either (a) grant a
  narrow, explicitly-labeled "demo:dispatch"-style claim per domain to a
  shared demo identity (weakens this project's own "one identity per real
  capability need" convention, `DevIdpSeeder.cs`), or (b) retire the
  generic cross-domain panel in favor of a per-domain demo action that
  already speaks the right claim (matching how Vitals/Meridian's own Queue
  screens already work) — deliberately left undecided here rather than
  picked unilaterally. In the meantime the *symptom* is fixed: a rejection
  now marks the outbox entry `Failed` (terminal, visible in the UI) instead
  of retrying forever silently, with no signal anything is wrong
  (`useOutboxStore.flush`'s new `permanentFailure` handling,
  `docs/changes/2026-09-02.md`).

- [ ] **Decide the authorization model**, now that the comparison doc is
  written: [`docs/comparisons/authorization-model.md`](docs/comparisons/authorization-model.md)
  surveys RBAC-extended, ABAC/policy-based (XACML/NGAC), ReBAC/
  relationship-tuple (Zanzibar-style), a named Hybrid, DACL, and
  Classification-based (Mandatory Access Control), each worked through
  one shared scenario (a Vitals PI resolving one assigned
  `AdverseEventReported` task) with a concrete schema+pseudocode sketch,
  per direct request, so a later real spike can start from this analysis
  instead of redoing it. Its Recommendation leans Hybrid (RBAC for
  coarse entity-type × access-level grants, a Zanzibar-shaped tuple table
  for per-task grants) but leaves the actual pick to the user. Once
  decided: write the deciding ADR (cites the comparison, doesn't
  re-derive it), add a row to `docs/comparisons/README.md`'s catalog, and
  only then close this item.

- [ ] **Decide scope for full OIDC/OAuth2 identity-provider support —
  now including an application-owned local authorization STS layer.**
  Today `EventStore.DevIdp` only implements `client_credentials` +
  RFC 8693 Token Exchange (`src/EventStore.DevIdp/Program.cs:133,143,
  279-280`) — no `authorization_code`/PKCE, no ID tokens ever issued
  (`Program.cs:412-414`), no interactive login, no userinfo endpoint. It
  exposes `/.well-known/openid-configuration` for discovery only
  (`ADR-006`). Per direct request, also design the split named and
  worked through in `docs/comparisons/authorization-model.md`'s
  "Application-owned local authorization STS" section: a central identity
  layer issues only generalized, cross-application roles; each deployed
  application runs its own local RFC 8693 token-exchange step mapping
  that generalized role into its own entity-type/access-level/per-task
  claims (real precedents verified: Okta's per-API custom authorization
  servers, Kubernetes `ClusterRole`+per-namespace `RoleBinding`). A
  completeness pass also found real standards for two adjacent gaps this
  same design direction depends on, both cited in the comparison doc's
  STS section now: RFC 7591/7592 (OAuth Dynamic Client Registration) for
  letting an application self-register instead of `DevIdpSeeder.cs`'s
  current hardcoded C# client list, and RFC 8414 (OAuth Authorization
  Server Metadata) as the OAuth-only counterpart to the OIDC Discovery
  doc already used. Decide what "full OIDC+OAuth2" and this STS split
  mean for this framework and record the decision in `docs/references.md`
  plus a queued ADR.

- [ ] **Evaluate adopting OpenID Federation as the multi-IdP trust
  pattern.** OpenID Federation 1.0 is now a Final spec (OpenID
  Foundation, approved 2026-02-17), split into a 1.1 core+profiles set
  approved 2026-05-06 — signed JWT Entity Statements chained to a Trust
  Anchor. This repo's existing "federation" mechanisms are all single-
  hop/pairwise, not trust-chain/hierarchy: `ADR-047` (one
  `TrustedFederationIssuer` per `AppId`, JWKS-verified), `ADR-082`
  (tenant-to-tenant data mapping), `ADR-044` (UCAN `AppTrustRoot`).
  Neither OpenID Federation itself nor a rejection of it has ever been
  evaluated here — decide and record adopted-or-rejected in
  `docs/references.md` either way, per this repo's own convention that a
  rejection needs to be as explicit as an adoption.

- [ ] **Decide where crontab-format and RFC 5545 (iCalendar) scheduling
  each apply, and record it.** No scheduler of any kind exists in this
  repo today — `ADR-069` explicitly declines to build one (`docs/adrs/
  adr-069-pluggable-outbox-flush-triggers.md:40`, "this framework doesn't
  build a scheduler, it only needs `Flush` to be safely callable by
  one"). Crontab format is POSIX.1 base + de facto Vixie-cron extensions
  (no RFC — don't cite one). RFC 5545 defines `VEVENT`/`VTODO`/
  `VJOURNAL`/`VFREEBUSY`/`VTIMEZONE`/`VALARM` plus the `RRULE` recurrence
  type. The two are genuinely complementary, not redundant: crontab
  fits a bare periodic engine tick with no calendar semantics (e.g. a
  re-screening interval); RFC 5545 `VEVENT`/`VTODO`+`RRULE` fits a
  person/appointment-facing domain object needing time, timezone,
  duration, and attendees as first-class data. **RFC 5545 alone is only
  the data format, not the scheduling protocol** — a completeness pass
  found the layer actually missing: RFC 5546 (iTIP) defines the
  REQUEST/REPLY/CANCEL/COUNTER methods for one party to propose/accept/
  decline a calendar object to another; RFC 4791 (CalDAV) is the HTTP
  transport for it; RFC 6638 (CalDAV Scheduling Extensions) is what
  actually binds iTIP's methods to CalDAV's HTTP transport specifically
  (vs. RFC 6047/iMIP's email transport, confirmed low relevance here) —
  without RFC 6638, adopting iTIP+CalDAV alone doesn't actually
  interoperate. RFC 7265 (jCal) gives a JSON form of the RFC 5545 data,
  likely a better fit than raw ICS text for this JSON-first framework.
  Decide which capability needs which (crontab vs. the RFC 5545/5546/
  4791/6638/7265 stack, or both for different purposes) and record in
  `docs/references.md`, likely with a new pattern doc/ADR.

- [ ] **Design a Contact/Profile entity before building vCard (RFC 6350)
  import/export.** No `Contact`/`Profile`-shaped entity exists anywhere
  in `src/` today (searched digital-identity-kyc domain and broader
  `src/` for `FirstName`/`GivenName`/`EmailAddress`/`PhoneNumber`/
  `Contact`/`Profile` — none found). vCard export needs something to
  export *from*; decide that entity's shape (likely in the digital-
  identity-kyc domain) before wiring RFC 6350 import/export on top of it.
  Same data-vs-protocol gap as the scheduling item above: RFC 6350 is
  data-only — RFC 6352 (CardDAV) is the HTTP access protocol (address-book
  collections, content negotiation between vCard 3.0/4.0), and RFC 7095
  (jCard) gives a JSON form, again likely a better fit here than raw
  vCard text.

- [ ] **Add "vehicle/equipment maintenance & fuel logs" as a candidate
  domain.** Real standards to ground it in: VMRS (ATA/TMC's hierarchical
  maintenance/repair coding system — industry-standard, no formal SDO)
  and ISO 15143-3/AEMP 2.0 (JSON/XML telematics data exchange covering
  position/hours/fuel/machine status — check whether this can reuse the
  existing `ADR-031` streaming/telemetry channel mechanism rather than
  needing a new one). ISO 14224's reliability/maintenance data categories
  are oil-and-gas-specific but may transfer conceptually. SAE J1939
  (verified, real) is the underlying heavy-vehicle CAN-bus wire protocol
  telematics data ultimately traces back to (SPNs like Fuel Level 1,
  Engine Speed) — the wrong layer for a fuel-log *event schema* itself,
  worth at most a one-line "traces back to" mention, not a driving
  citation; VMRS/ISO 15143-3 remain the right fit for the schema layer.
  Add at minimum
  a "considered" domain doc (`docs/domains/vehicle-equipment-maintenance/
  README.md`, one feature doc, per the existing 13-considered/2-chosen
  structure) and a row in `docs/domains/README.md`'s catalog and
  `docs/comparisons/proving-ground-domain.md`'s coverage matrix.

- [ ] **Recover a lost item: reconcile `06-solution-structure.md`'s
  project list against what was actually built.** `docs/06-solution-
  structure.md:42-44` states "reconciling this entire file's project
  list against what was actually built, item by item... tracked in
  `TODO.md`, not attempted here" — but no such item exists in `TODO.md`
  (confirmed via grep). Found during a vision-completeness audit this
  session; exactly the "flagged in passing, never actually added"
  failure mode `CLAUDE.md` already names as having happened once before
  (`ChannelOrigin.Origin`/`OriginId`). Walk `06-solution-structure.md`'s
  project sketch against the real `src/` tree and correct any drift.

- [ ] **Exercise the SDK codegen story end to end — nothing has ever
  actually been published or consumed.** `ADR-054` (Kiota for OpenAPI,
  GraphQL Code Generator for TypeScript, Strawberry Shake for .NET
  GraphQL clients) and `ADR-062` (SemVer 2.0.0 for every `EventStore.*`
  package) are both real, Accepted designs — but `ADR-062`'s own
  "Implementation note, added 2026-08-12" says plainly that no package
  has ever actually been published to a real registry, and `ADR-080`
  independently confirms npm/NuGet provenance signing is unbuilt for the
  identical reason (nothing exists yet to sign). Per this repo's own
  standing rule that a build succeeding isn't the same bar as actually
  running the thing: publish one real package to a real (or realistic
  local) registry, generate a client against it with the tool `ADR-054`
  names, and confirm it actually works — the entire "genuinely reusable
  by an outside team" story currently rests on an unverified assumption.

- [ ] **Write a getting-started/quickstart doc.** No file anywhere in
  this repo walks a new team through standing up a `EventStore.Host.*`
  project and registering their first event type end to end —
  `06-solution-structure.md`/`08-build-plan.md` are architecture/
  dependency references, not an onboarding doc, and `ADR-062` calls the
  three Host projects "reference implementations/quickstart templates"
  but that's code, not a walkthrough. Found during a vision-completeness
  audit: `README.md`/`docs/naming.md` explicitly want this framework to
  read as "a plausible real infra-product... in the company of things
  like Kafka, Temporal" — every one of those has a real quickstart doc.
  While writing it, also state the framework's language/platform scope
  explicitly (today only inferable from `ADR-054`/`062` naming .NET +
  TypeScript as the only built client targets) — no doc anywhere
  currently says this is a deliberate boundary rather than an oversight.

## Design-phase program, per direct request — run in this order

Five sequenced phases; each depends on the previous one's completion.
Don't start a later phase until the one before it is actually done, per
direct instruction ("once that is completed... once the designs are
improved..."). **This entire program is design-phase work — docs and
ADRs only, no implementation/code changes** (direct instruction: "we are
currently still just working on a secondary design phase and not actual
implementations at this time").

**Standing rule for every phase below, per direct instruction: a found
gap/duplicate/conflict is not automatically a mistake to silently fix.**
Something that looks missing may have been intentionally descoped or
removed earlier — ask rather than assume when it's genuinely ambiguous
which is true. Only resolve unilaterally what's objectively, verifiably
a drift/error (the same bar the `08-build-plan.md` row 40 fix and the
`06-solution-structure.md` recovered item already met — each had direct
textual evidence, not judgment calls). Everything else surfaces as a
question — either a `docs/10-open-questions.md` row (if it's a real
design fork) or a direct question back to the user (if it's really "was
this on purpose") — never a silent correction.

- [ ] **Phase 0 — missing-documents sweep.** `docs/patterns/README.md`
  has a real, large backlog: **35+ rows still marked "Catalog only"**
  (a landed, Accepted ADR exists; the full pattern write-up — general
  pattern explained, cited, PlantUML/Salt diagram, then how this design
  applies it — was never written). Grep `docs/patterns/README.md` for
  "Catalog only" for the current, authoritative list rather than
  re-deriving it here (it will drift). Also covers the two doc gaps
  already found this session and tracked above (getting-started/
  quickstart doc; the `06-solution-structure.md` project-list
  reconciliation). Large enough to need `.claude/protocols/
  parallel-batch-dispatch.md`'s batching approach, not one pass.

- [ ] **Phase 1 — full ADR review: find and resolve missing, duplicate,
  and conflicting ADRs.** 103 ADRs exist (`docs/adrs/adr-001-*.md`
  through `adr-103-*.md`, confirmed no gaps in numbering). A prior
  session already did a full compliance review + fresh-eyes contradiction
  hunt at the 74/75-ADR mark (`docs/changes/2026-07-30.md`) — this is a
  **fresh** full pass at the current 103, since drift has kept happening
  since then (this session alone found `ADR-036`'s undecided drift and
  `08-build-plan.md` row 40's stale status). Three things to find and
  fix, per direct request: **missing** ADRs (a decision clearly implied
  or already made in prose/code but never formalized as its own ADR —
  create it), **duplicate** ADRs (two ADRs deciding the same thing,
  possibly with drifted answers — consolidate via `.claude/protocols/
  additive-history-editing.md`, never silently delete history), and
  **conflicting** ADRs (two ADRs whose decisions contradict — resolve
  the same additive way, one correcting the other in place with a dated
  note, per this repo's own standing convention). Use the parallel-batch
  protocol, split by ADR-number range; consolidate cross-range findings
  centrally before writing any fix, since a conflict by definition spans
  more than one agent's own range.

- [ ] **Phase 2 — review and flesh out the proving-ground domains.**
  Once Phase 1 is done: review `docs/domains/clinical-trials-device-
  telemetry/` (Vitals) and `docs/domains/digital-identity-kyc/`
  (Meridian) for depth gaps against their own stated 5-feature-doc/
  4-workflow (Vitals) and 4-feature-doc/3-workflow (Meridian) structure,
  and flesh out wherever genuinely lacking.

- [ ] **Phase 3 — identify cross-domain functionality that belongs at
  framework level.** Once Phase 2 is done: look for functionality built
  or designed for Vitals/Meridian specifically that is actually generic
  and not currently called out as such — a real candidate for promotion
  from a domain doc into the Duplex framework/engine level (an ADR,
  pattern doc, or core mechanism) rather than staying domain-scoped.
  Name each candidate found and propose the promotion explicitly (new
  ADR/pattern doc) rather than moving code silently.

- [ ] **Phase 4 — write or update an architecture/design compliance
  guideline.** Once Phase 3 is done: a guideline doc (new, or an update
  to an existing one if Phase 1/3 reveals a better home) stating the
  cross-cutting conventions every ADR/pattern/domain doc and every real
  implementation should already be complying with — consolidating what's
  currently scattered across `CLAUDE.md`'s "Conventions established so
  far" section and this repo's own `.claude/protocols/*.md` files into
  something aimed at compliance/consistency checking specifically, per
  direct request.

- [ ] **Phase 5 — configure linting/static-analysis tooling to enforce
  the guideline.** Once Phase 4 is done: wire up real tooling (.NET
  analyzers/`.editorconfig`/Roslyn analyzers for `src/`, ESLint/similar
  for `client-web/`) configured, where mechanically possible, to enforce
  the guideline's own preferred patterns — ask the user for their
  specific preferred patterns/rules before configuring, rather than
  guessing a generic ruleset.
