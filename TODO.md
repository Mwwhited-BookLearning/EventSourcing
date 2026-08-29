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

- [ ] **Reduce routine Postgres `40001` conflict frequency under real
  concurrent load — properly, not via a quick patch** (direct report:
  "I'm getting a ton of errors... it should not error this much. it
  should be much more graceful for retrys and so on"). Investigated
  this session; **do not re-attempt either approach below without
  reading this first — both were tried, both failed, for specific,
  understood reasons:**
  - Confirmed real: a live `AppHost` + `docker logs <postgres-container>`
    shows routine `ERROR: could not serialize access due to read/write
    dependencies among transactions` under nothing more than the two
    proving-ground Simulators' own background ticks, not just heavy
    load. `EventStore.Host.Postgres`'s existing `EnableRetryOnFailure`
    (`maxRetryCount: 20`, `errorCodesToAdd: ["3D000","40001"]`) already
    retries every one of these correctly and never lets it reach a
    caller — this is a noise/gracefulness problem, not a correctness
    bug; don't weaken or remove that retry config while working this.
  - Root cause: `EventAppender.AppendAsync`/`AccessLogAppender.
    AppendAsync`'s Serializable-isolation "read the tail, insert,
    compute chain" critical section (`ADR-019`/`ADR-033`/`ADR-045`) is
    exactly the read/write shape Postgres's SSI flags whenever two
    appends overlap.
  - **Attempt 1 (failed, ineffective): `pg_advisory_xact_lock` acquired
    as the first statement AFTER `BeginTransactionAsync(Serializable)`.**
    Proven ineffective by a dedicated regression test (30 concurrent
    publishers, counting EF's own `CoreEventId.ExecutionStrategyRetrying`
    diagnostic event) — still observed ~97 real retries out of 30
    publishers. A transaction-scoped lock acquired after `BEGIN` doesn't
    prevent two Serializable transactions from being concurrently open
    in the first place, which is what SSI actually conflicts on;
    waiting *inside* one doesn't un-open it.
  - **Attempt 2 (failed, unsafe — caused a real deadlock): a
    session-scoped `pg_advisory_lock`/`pg_advisory_unlock` pair acquired
    *before* `BeginTransactionAsync` and released in a `finally`.**
    Theoretically sound (guarantees at most one session inside the whole
    BEGIN..COMMIT window), but the same 30-concurrent-publisher
    regression test hung indefinitely (>180s, had to be killed) —
    suspected interaction between session-scoped advisory locks and
    Npgsql's connection pooling/EF's retry-triggered connection
    resets (a lock acquired on one physical connection can outlive that
    connection's return to the pool if a retry swaps connections
    mid-attempt, or similar). **Not safe to retry without solving that
    interaction first, and not worth solving blind** — reverted
    immediately rather than risk shipping a deadlock.
  - **The real fix, not yet built, needs care**: drop this specific
    critical section from `Serializable` to `ReadCommitted` and use
    `SELECT ... FOR UPDATE` (raw SQL, no EF LINQ operator for it) on the
    tail row instead — Postgres's Read Committed semantics correctly
    block a second locker and re-fetch the FRESH row once unblocked
    (unlike Serializable, whose snapshot is fixed regardless of any
    lock-wait, which is *why* attempts 1 and 2 above couldn't work no
    matter how the lock itself was structured — under Serializable,
    `FOR UPDATE` on a since-changed row throws its own 40001
    ["could not serialize access due to concurrent update"] instead of
    silently returning stale data). This needs: (1) raw SQL via
    `Database.SqlQuery<T>`/`FromSqlInterpolated` for the `FOR UPDATE`
    read (a `record` projection matching `ChainHash`/`LogicalClock`
    column names — verify exact Npgsql quoted-identifier casing before
    assuming it matches the C# property names); (2) confirming dropping
    to `ReadCommitted` for just this critical section doesn't weaken any
    OTHER guarantee this method relies on Serializable for (re-read
    `ADR-019`/`ADR-033`'s own reasoning first); (3) the identical
    regression-test technique (count `ExecutionStrategyRetrying`, assert
    zero, prove red-then-green by temporarily reverting) *before*
    trusting it, given both easier-looking attempts above silently
    failed one way or another; (4) SQLite/SQL Server are NOT reported to
    have this problem and don't need a matching change — Postgres-only,
    same scoping as both failed attempts.

Direct request: add PlantUML sequence diagrams to every UI playbook, add
a per-application (Vitals/Meridian) README covering that application's
workflows and how they interact, rename every playbook file to a
`{role}-{task}.md` scheme (dropping the `{workflow}-{feature doc name}.md`
convention), and expand each application with more of the proving-ground's
own defined use cases. In progress, tracked here so nothing gets lost
mid-pass:

- [x] **Add a PlantUML sequence diagram to each of the 9 original
  playbooks** — done, all 9 (`VitalsWorkflowAPlaybookTests` through
  `MeridianWorkflowBRelyingPartyAccessPlaybookTests`). The 2 new
  queue-decision playbooks below get theirs inline as they're built (PI
  Queue done; KYC Analyst Queue still needs one).
- [x] **Restructure every playbook to `{domain}/{role}/{task}.md`** —
  done. Went through two revisions on direct feedback: first
  `{role}-{task}.md` (dropping `{workflow}-{feature doc name}.md`
  entirely), then role moved into its own directory segment rather than
  a filename prefix. All 10 playbooks (9 original + the new PI Queue
  one) generate at their final nested paths, verified together against
  a live `AppHost`. Every stale generated file/asset folder under an
  older name (`git rm` for the originally-tracked `workflow-*.md` set,
  plain `rm` for the untracked intermediate flat `{role}-{task}.md` set
  that never got committed) is gone. `docs/playbooks/README.md`'s
  catalog rewritten to the new paths, split into per-domain tables.
- [x] **Add 2 new playbooks using already-built Queue UI** — done, both.
  Genuine additional proving-ground use-case coverage that needed no new
  production UI, unlike the Relying-Party Access panel:
  1. Vitals' Principal Investigator Queue (`VitalsPiQueue.vue`) —
     `VitalsPrincipalInvestigatorQueuePlaybookTests`, verified against a
     live `AppHost`. Found and fixed a real, previously-undiscovered bug
     doing it: `publishClient.ts`'s RFC 9470 step-up-retry check read
     `body.error`, but `PublishEndpoints.cs`'s actual 401 response is an
     RFC 7807 `ProblemDetails` body with no `error` field at all (the
     real field is `title`) — every step-up retry through this client
     silently fell through to an ordinary failure before this fix. Also
     corrected `useEventComposer.spec.ts`'s own mock, which had been
     matching the bug's assumption, not the real server response.
  2. Meridian's KYC Analyst Queue (`MeridianAnalystQueue.vue`) —
     `MeridianKycAnalystQueuePlaybookTests`, verified against a live
     `AppHost` (accept/reject a pending `SanctionsScreeningPerformed`
     match `Samples.Meridian.Simulator` publishes every ~25s, alternating
     matches roughly 1 in 3 ticks). Found and fixed a second real bug in
     the same family: `AuthorityQueue.vue`'s own `summarize()` rendered a
     masked field (`MatchedName`/`MatchedListEntryId`'s `{value, masked,
     erased}` wrapper) as the literal, useless string `"[object Object]"`
     — the first time this queue was ever exercised with a masked field
     actually present in the payload. Fixed to match
     `EntityBrowser.vue`'s own already-correct `"[masked/complex]"`
     handling; new regression test added
     (`AuthorityQueue.spec.ts`).
- [x] **Vitals' Workflow C (Trial Data Export and Subject Rights)** —
  done: `LineageExportAndPlaybackPanel.vue`, a new domain-agnostic
  "Lineage & Playback" tab wiring `BitemporalPlaybackControl.vue`/
  `OfflineBundleViewer.vue` together with `exportLineage`/
  `downloadBundle`/`playbackAsOf` (all of which already existed, unused).
  `VitalsWorkflowCLineageExportAndPlaybackPlaybookTests` verified against
  a live `AppHost` — export, bundle verification, full event list, and a
  real System-Time Playback reconstruction all demonstrated for real.
  Found and fixed two more real bugs the same way as the queue playbooks'
  own two: `parseNdjson` (`bundle.ts`) never actually remapped the
  server's real PascalCase NDJSON output to the camelCase shape every
  downstream consumer assumed (a bare, unchecked type assertion) — every
  field was silently `undefined` until `verifyBundle`'s own date parsing
  threw; and `PlaybookRecorder.RecordStepAsync`'s screenshot was
  viewport-only, silently cropping this panel's own playback result
  (which sat below the fold on a page taller than one screen) despite
  its own visibility assertion passing — fixed to `FullPage: true`, all
  12 playbooks regenerated under it. The erasure half of this workflow
  (`EntityErasureRequested`) was not investigated this pass — worth
  checking whether the already-generic Event Composer tab already
  reaches it before assuming it needs its own UI too.
- [x] **Create `docs/playbooks/vitals/README.md` and `docs/playbooks/
  meridian/README.md`** — done. Each lists its own domain's workflows
  and playbooks, plus a PlantUML object diagram showing how they
  actually interact through shared entities: Vitals' four workflows
  around one continuity subject (`S-0091`, several loosely-related
  entities linked by business `SubjectId` fields, not `ADR-005` causal
  parent links); Meridian's three workflows all folding onto the exact
  same `ApplicantIdentity` entity, with Workflow B (Relying-Party
  Access) a genuine data dependency on Workflow A's own event, not just
  a shared subject. `docs/playbooks/README.md`'s own catalog now points
  to both rather than restating their content.

- [ ] **Create `style-guide.md` describing how `client-web`'s UI/UX
  should work** (direct request), with example screens either as
  PlantUML+Salt mockups or as real pages captured via a Playwright
  script that keeps the file updated (this project's own established
  `PlaybookRecorder` mechanism, reused). Deliberately sequenced AFTER
  the Naive UI/left-nav item below, not before: a style guide describing
  the TARGET UI/UX would need rewriting the moment that adoption lands
  if written against today's plain-HTML shell first. Not yet started.

- [ ] **Adopt Naive UI (`naiveui.com`) and a left-hand-nav shell
  (Azure Portal/Azure DevOps-style), replacing `client-web`'s current
  plain-HTML tab-button styling entirely — direct request, deliberately
  sequenced AFTER the diagram/rename/README/expansion work above, not
  alongside it.** In progress: `naive-ui@2.45.3` + `vue-router@5.3.0`
  installed (`npm audit` clean), user confirmed via `AskUserQuestion`
  both (a) restyle every existing component with real Naive UI
  components in the same pass as the shell, not just the shell, and (b)
  introduce real Vue Router routes rather than staying tab-switcher/
  router-free. Corrected a misattribution made while scoping this: the
  "plain tab switcher, not a router dependency" note is a code comment
  in `App.vue` citing the "Proving-Ground Application UX" build-plan
  item, NOT `ADR-039` — `ADR-039` never mentions routing at all, so the
  new ADR below documents this as a fresh decision, not an `ADR-039`
  revision. `docs/libraries/README.md` already carries a Naive UI row
  (citing `docs/patterns/mvvm-client-architecture.md`'s Styling layer,
  `theme/tokens.js` + `themeOverrides`) — that row describes the
  **theming** layer only, predates any actual install, and doesn't
  mention navigation/routing/data-grids at all, so it needs its "Adopted
  in" column updated to the new ADR, not replacing/contradicting it.
  Remaining work: (1) write the new ADR (next number `ADR-099`) covering
  Naive UI adoption, Vue Router adoption, the left-nav shell, AND the two
  new items below (grid pagination, chart-view configuration) as one
  coherent client-architecture decision; add its index row to
  `docs/07-adrs.md`; (2) redesign `App.vue`'s top-nav tab-button shell
  into a left-hand navigation rail with real routes for Detail/Browse/
  Composer/Queue/Relying-Party/Lineage; (3) restyle every existing
  component (`EntityView`/`GenericFallbackView`, `EntityBrowser`,
  `EventComposer`, the two Queue components, `RelyingPartyAccessPanel`,
  `LineageExportAndPlaybackPanel`, `BitemporalPlaybackControl`,
  `OfflineBundleViewer`) with real Naive UI components in the same pass;
  (4) update every Vitest component spec whose selectors break against
  the new markup; (5) update every one of the 12 Playwright playbook
  tests' own navigation selectors to match the new left-nav/router
  markup, then re-verify all 12 together against a live `AppHost`; (6)
  full solution build + full Vitest suite + full Playwright suite
  regression, commit, push.

- [ ] **Data grids: real pagination, not just client-side paging over an
  already-fully-loaded cache** (direct request, found while starting the
  Naive UI pass above). `EntityBrowser.vue` (and both Queue components)
  render 100% of `useEntityCacheStore`'s in-memory cache today — that
  cache is fed by a `mode: REPLAY` GraphQL *subscription* tail
  (`useEntityViewActions.ts`'s `subscribe()`), not a paged query, so
  there is currently no server mechanism to request "page 2" at all; the
  data is already fully resident client-side by the time any grid
  renders it. Two distinct halves, sequenced together but worth keeping
  separate in the writeup: (a) **near-term, in the Naive UI pass itself**
  — use `n-data-table`'s built-in pagination prop against the existing
  in-memory array, which at least bounds DOM/render cost per page (real,
  but does NOT reduce what's sent over the wire, since REPLAY mode
  already streamed everything); (b) **the actual "less data returned to
  the client" ask** needs a genuine new server capability — a paged
  entity-list GraphQL query (cursor-based, matching HotChocolate's own
  convention already used elsewhere in this schema) as an alternative to
  always subscribing in `REPLAY` mode, plus a client composable that
  fetches one page at a time instead of accumulating the whole cache.
  This is real new server + schema work, not a UI-only change — needs
  its own scoping pass (which GraphQL query shape, whether it coexists
  with or replaces `REPLAY` mode for large entity sets) before coding;
  do NOT improvise the query shape without that pass. Covered by the
  same `ADR-099` as the Naive UI shell (one client-architecture
  decision), but (b)'s query-shape design is real, separate work, not a
  restyle.

- [ ] **Configurable charting view for display elements** (direct
  request, found at the same time as grid pagination above). Some
  fields/views (the user named "graphs" — likely this project's own
  time-series-shaped data, e.g. Vitals' IONM alert telemetry or
  screening-history trends, not a literal existing "graph" component,
  since none exists yet) would read better as a chart than as a table
  row. Needs: (1) a charting library decision — Naive UI ships no
  charting components of its own, so this is a new dependency choice,
  same "buy over build"/"verify before citing" bar as Naive UI itself,
  with its own `docs/libraries/web/{library}.md` entry once chosen; (2)
  a declarative way to mark a field/view as chart-renderable (a
  `chartable`/`chartType` config alongside the existing `.columns.js`/
  `.fields.js` ViewModel-structure convention `docs/patterns/mvvm-
  client-architecture.md` already establishes, not a one-off per
  component); (3) at least one real example wired against real data
  (Vitals' IONM/device telemetry is the natural first candidate, given
  its own time-series shape already discussed in `docs/domains/
  clinical-trials-device-telemetry/`). Deliberately sequenced as
  follow-on work after the Naive UI shell/restyle lands, not folded into
  it blind — picking a charting library and a config schema is its own
  real decision, not a restyle detail.
