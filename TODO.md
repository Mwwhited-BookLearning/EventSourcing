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

Phase 0 (missing-documents sweep) is **done** — all three parts closed:
the `docs/patterns/README.md` 41-row backlog, `docs/getting-started.md`
(new, linked from `README.md`'s document index, including the
language/platform scope statement it was asked to state explicitly), and
`06-solution-structure.md`'s project-list reconciliation (12 real
projects that had zero mention anywhere in that file — `EventStore.Flows`,
`.WorkerWakeSignal`, `.SqlClr.SqlServer`, `.Benchmarks`, and all eight
`Samples.Vitals*`/`Samples.Meridian*` projects — added in place). Full
narrative in `docs/changes/2026-09-02.md`. **Phase 1 (the full ADR
review) is next.**

**Phase 1 (full ADR review) is now fully done**, all 21 Tier A findings
and all 3 Tier B judgment calls resolved (the user answered all three
directly: yes to `ADR-010`↔`ADR-037` cross-referencing, literal
strikethrough for `ADR-011`, yes to correcting `distributed-correctness-
testing.md`). Full narrative in `docs/changes/2026-09-02.md`. No missing
or duplicate ADRs were found anywhere across the 103-ADR corpus.

**Phase 2 is next.**

**Phase 2 is done.** A read-only audit (2 parallel agents, one per
domain) found both domains structurally excellent — every feature doc
fully populated against the template (context, sequence/ER diagrams,
Salt mockups, substantive multi-scenario Gherkin), all workflows
correctly wired, Special Concerns and Glossaries both substantive. Four
real, code-confirmed findings, all fixed: two feature docs per domain
described a mechanism that was never built exactly that way (Vitals'
`ConsentApproval`/`ConsentApprovalResolver` reuses the shared
`authorityDecision` reactor instead; Vitals' `IonmAlertRaised` is
`ChangeKind: "Partial"` not `"Full"`, a real fold-ordering fix; Meridian's
Workflow A has no Router-initiated token exchange, self-attestation goes
straight to `unattested`; Meridian's Workflow B has no `accessGrant`
event or generic entity-query field, delegation is a client-signed
`UcanDelegation` instead) — all four divergences were *already*
documented centrally in `docs/domains/README.md`'s own run-and-found-
divergences list, just never cross-referenced from inside the specific
feature doc a reader would actually open; added those cross-references.
Also added 5 ADRs to Meridian's `README.md` `## Applicable ADRs` that
were demonstrably load-bearing throughout its own feature docs but
missing from the summary list (`ADR-008`, `066`, `079`, `096`, `101`).
Full narrative in `docs/changes/2026-09-02.md`.

**Phase 3 is next.**

**Phase 3 is done.** A read-only cross-domain review found two real
candidates (and correctly ruled out two others as already properly
resolved — `AuthorityQueue.vue` is already generic, not duplicated; the
"secondary opinion" terminology echo is already a promoted, shared
mechanism). Both real candidates handled: a new pattern-interaction doc
(`docs/patterns/interactions/claim-gated-step-up-signoff.md`, pure docs,
written this session) documenting the "capture → claim-gated decision →
step-up sign-off → authoritative fold" composition both domains build
identically; and a framework-level registration-helper promotion
(needs a decision + a code change, tracked as its own item above since
code is out of scope this session). Full narrative in `docs/changes/
2026-09-02.md`.

**Phase 4 is next.**

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

- [ ] **One-line code-comment fix, deferred only because this session is
  design-phase-only (no `src/` changes).** `src/EventStore.Domain/
  LeaderElection/LeaderLease.cs`'s own header comment still lists
  `"UpcastMaterializer"` as a valid `WorkerRole` value (never used —
  confirmed via repo-wide grep) and omits `"ExpectedResponseWatcher"`
  (the real 4th role, `ADR-094`) — found during Phase 1's ADR review,
  already fixed in `ADR-078`'s own text (both the Decision bullet and
  its code sample), just not in the shipped file itself. Update the
  comment to `"Router" | "PeerSyncOutboxPump" | "WebhookOutboxPump" |
  "ExpectedResponseWatcher"` once code changes are back in scope.

- [ ] **Promote the duplicated "ensure claim on shared `authorityDecision`
  type" registration helper into `EventStore.SchemaRegistry` itself —
  needs a decision, then a code change (deferred, design-phase-only).**
  Found during Phase 3 (cross-domain-to-framework review):
  `src/Samples.Vitals/VitalsSharedTypes.cs`'s and `src/Samples.Meridian/
  MeridianSharedTypes.cs`'s `EnsureAuthorityDecisionRegisteredAsync`
  methods are byte-for-byte identical in schema and near-identical in
  body, called from every Vitals/Meridian workflow that needs a decision
  reactor. This duplication already has a real, observed cost, not just
  a hypothetical one: Vitals' copy hardcodes a `RequiredSignature`
  parameter, Meridian's doesn't, so when Meridian's Workflow C needed
  step-up on its own decision it couldn't extend the shared type the
  way Vitals did — it had to hand-register a wholly separate event type
  (`SarFilingRecorded`) instead. Promote to something like
  `SchemaRegistryService.EnsureClaimOnReservedTypeAsync(appId, typeName,
  jsonSchema, publishClaim, requiredSignature?)`, documented in
  `docs/patterns/interactions/claim-gated-step-up-signoff.md` (written
  this session, already flags this exact gap). The reactor this
  registers for (`AuthorityDecisionResolver`) is already genuinely
  framework-level; only the registration convenience is duplicated.
