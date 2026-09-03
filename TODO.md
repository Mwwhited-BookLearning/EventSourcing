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
  closed the OTHER half, the field-casing gap, for real). **Decided by
  `ADR-105`, code still pending (design-phase-only)**: neither of the
  original two options (a shared claim weakening "one identity per real
  capability need," or retiring the generic panel) — the demo identity
  instead gets a generalized `"demo"` role as a JWT claim, and each
  domain's own local STS/middleware (`ADR-105`'s per-application
  expansion step) maps `"demo"` into that domain's actual
  `RequiredClaims` (`patient:enroll`, etc.) the same way a real
  `"clinician"` role would. The demo identity itself never holds a
  domain-specific claim directly — it only ever sees the generalized
  role, exactly matching `ADR-105`'s own model, not a special case built
  for this gap alone. Needs the generalized-role layer and at least one
  domain's STS/middleware expansion actually built before this can
  close — tracked here until then, not a design decision anymore.

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

The five-phase design-review program (missing-documents sweep, full ADR
review, proving-ground domain review, cross-domain-to-framework review,
architecture/design compliance guideline) plus Phase 5 (linting/static-
analysis tooling) are all **done** — per this file's own workflow,
deleted from here rather than kept as completion narratives; the full
account of each is in `docs/changes/2026-09-02.md` (Phase 0) and
`docs/changes/2026-09-03.md` (Phase 1 onward — split across the two
files since work crossed a real midnight boundary mid-session).

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
