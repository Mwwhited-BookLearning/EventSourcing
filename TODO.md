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

- [x] **Create `style-guide.md` describing how `client-web`'s UI/UX
  should work** — done. `docs/style-guide.md`, real pages captured via a
  new `StyleGuideTests.RecordStyleGuide` (`tests/EventStore.E2ETests/
  StyleGuideTests.cs`), not PlantUML+Salt mockups — ADR-099 already built
  the real target UI, so a hand-drawn mockup would just be a less
  accurate second copy. Reused `PlaybookRecorder` exactly as directed,
  extending it with one new `AddSection(heading, markdown)` method for
  prose-only sections with no screenshot of their own (design tokens,
  accessibility conventions) — backward-compatible, every existing
  playbook's own `RecordStepAsync`/`WriteMarkdownAsync` call is
  unchanged. Six sections: left-hand nav shell, design tokens/theming,
  data tables with pagination, forms, cards/panels, property tables
  (deliberately still plain HTML), accessibility baseline — covering
  every Naive UI component family this app actually uses via one
  `client-web-vitals` instance (Meridian's Relying-Party panel skipped as
  a screenshot subject: same `n-card`/`n-form`/`n-button` primitives
  already shown, not a new pattern). Found and fixed one real bug doing
  it: the Compose screen's capture originally waited only for the
  "Event Composer" heading, which is present even during "Loading
  registered event types..." — caught by actually reviewing the
  screenshot (blank form), fixed by waiting for the real `<select>`
  instead, matching this project's own repeated "verify by looking at
  the actual output" lesson.

- [x] **Adopt Naive UI (`naiveui.com`) and a left-hand-nav shell
  (Azure Portal/Azure DevOps-style), replacing `client-web`'s current
  plain-HTML tab-button styling entirely** — done, `ADR-099`
  (`docs/adrs/adr-099-naive-ui-router-left-nav-shell.md`, indexed in
  `docs/07-adrs.md`). `docs/libraries/README.md`'s Naive UI row's
  "Adopted in" updated to cite it (theming-layer description kept, since
  it was already correct); a new Vue Router row added
  (`docs/libraries/web/vue-router.md`).
  - `App.vue` rewritten: `appConfig.ts`/`appState.ts` extracted (config/
    `queueDomain` at module scope so `router.ts`'s navigation guard can
    read them without depending on component setup timing; business
    state provided via `provide`/`inject` to six new route-view
    components under `src/views/`). `n-config-provider` +
    `n-layout`/`n-layout-sider`/`n-menu` left-hand rail replaces the top
    tab-button row; `n-menu` items are render-function `RouterLink`s
    (real `<a href>`, not a plain click handler) so deep-linking and
    screen-reader link semantics both work. Sidebar collapse persisted
    to `localStorage` (try/catch-wrapped). Vue Router installed with one
    route per former tab; domain-gated routes (`/queue`, `/relying-
    party`) redirect to `/detail` via `router.beforeEach` when
    `queueDomain` doesn't match, replacing the old template `v-if` gates.
  - Every existing component restyled with real Naive UI components in
    the same pass, per the user's confirmed choice:
    `GenericFallbackView`/`EntityBrowser`/both Queue components (via
    `AuthorityQueue.vue`)/`EventComposer`/`RelyingPartyAccessPanel`/
    `LineageExportAndPlaybackPanel`/`BitemporalPlaybackControl`/
    `OfflineBundleViewer`. `EntityBrowser` and `AuthorityQueue` use
    `n-data-table` with its own built-in pagination (page size 10) —
    `AuthorityQueue`'s per-row Reason/Meaning inputs and Accept/Reject
    buttons are column render functions (`h(NInput, ...)`/`h(NButton,
    ...)`), not plain cell text. The event-type `<select>` in
    `EventComposer` and the numeric amount field deliberately stayed
    native HTML (documented inline) — `n-select`'s teleported dropdown
    and a numeric `n-input` variant would have needed a disproportionate
    test rewrite for no real UX gain here.
  - Every Vitest spec whose selectors broke against the new markup
    fixed (`GenericFallbackView`, `EventComposer`, `AuthorityQueue`,
    `EntityBrowser`); all 9 reference-app spec files / 42 tests plus
    `mvvm-client`'s own 121 pass. All 12 Playwright playbooks' nav clicks
    updated from `GetByRole(AriaRole.Button, Name: "Browse")`-style
    top-tab clicks to `GetByRole(AriaRole.Link, ...)` (confirmed via a
    real run's own captured ARIA snapshot: `n-menu` items render as
    `menuitem > link`, not buttons); all 12 pass together against a live
    `AppHost` (`dotnet test tests/EventStore.E2ETests`, ~5.5 min).
  - **Five real bugs found only by actually running things, not by
    reading the code back** (this project's own most-repeated lesson,
    holding again):
    1. `n-card`'s `title`/`header` renders `role="heading"` with no
       `aria-level` in this Naive UI version — a genuine axe-core
       critical violation, caught by `a11y.spec.ts`'s own real
       `GenericFallbackView` check. Fixed by using a real `<h2>` instead
       of the `title` prop everywhere a heading was needed.
    2. `aria-label` on `n-card`'s own root `<div>` (no ARIA role) is a
       real axe-core `aria-prohibited-attr` finding — moved to a
       wrapping `<section aria-label="...">` instead, everywhere this
       pattern occurred.
    3. `n-form-item`'s `label` prop never actually associates via `for`/
       `htmlFor` in this Naive UI version (confirmed by reading
       `node_modules/naive-ui`'s own `FormItem.mjs` — no such wiring
       exists) — `label-for` is not a real prop and silently no-ops;
       `RelyingPartyAccessPanel`'s own Playwright playbook (`GetByLabel`)
       caught this for real. Fixed with an explicit `aria-label` on each
       input matching its visible label text, everywhere this pattern
       occurred, not just the one field the test happened to touch.
    4. Naive UI's `input-props`/`inputProps` TypeScript type has no
       index signature for arbitrary attributes (`data-testid`, `id`,
       `aria-label` all rejected at compile time by `vue-tsc`) — worked
       around with an explicit `as any` cast at each call site.
    5. Adding `n-data-table` pagination to `EntityBrowser` made a
       specific, already-known `EntityId` (the seed data's own
       continuity subject) genuinely undiscoverable once the
       long-running proving-ground simulator pushed more than a page of
       newer entities in front of it — caught by
       `VitalsWorkflowAPlaybookTests` failing for real against a live
       simulator, not a static fixture. Fixed with a client-side filter
       box (`EntityBrowser.vue`) over the same already-loaded array — the
       minimum fix that makes pagination usable rather than just
       smaller; 8 of the 12 playbooks' own row-lookup steps updated to
       fill the filter before asserting a specific row is visible.
  - Full regression green: `dotnet build EventStore.slnx` (0 errors),
    full `client-web` Vitest suite both workspaces (63 tests), full
    `EventStore.E2ETests` Playwright suite (12/12, run together against
    one live `AppHost`).

- [x] **Data grids: a real paged server query** (direct request) —
  **server half done**, client wiring precisely scoped below, not yet
  started. Scoping pass first confirmed the TODO's own assumption was
  wrong before building anything: `LineageQueries.cs`'s own comment
  documents this exact schema already having a real precedent, and it's
  plain `first`/`skip` int arguments, NOT a HotChocolate `[UsePaging]`
  Relay Connection (that comment: "honestly narrower than a full Relay
  cursor implementation" — deliberate, not an oversight) — matched that
  precedent instead of the cursor-based shape originally assumed here.
  - `EntityQueryTypeModule.cs`: two new sibling fields per (AppId,
    EntityType) group, alongside the existing `entity_{appId}_{entityType}
    (id)` — `entities_{appId}_{entityType}(first, skip)` (a real
    `Skip()/Take()` over `LiveEntityStore`, `ORDER BY EntityId`, EF-
    translated to SQL `OFFSET`/`FETCH` — genuine server-side paging, not
    a client-side slice of an already-fetched set) and
    `entityCount_{appId}_{entityType}` (total count, for `n-data-table`'s
    own page-number UI). Also added a new `entityId` field to the shared
    entity envelope (`BuildEntityEnvelopeFields`) — found necessary only
    by actually running the new query: the single-entity query never
    needed one (the caller already supplies `id`), but a LIST has no
    such per-row argument, so callers had no way to tell which row was
    which without it (a real HotChocolate "field does not exist" error
    caught this, not assumed upfront).
  - Deliberately queries `LiveEntityStore` only, never overlaid with the
    authoritative `EntityStore` per row like the single-entity query
    does — `LiveEntityStore` is unconditionally populated for every
    entity ever folded (`ADR-042`), so it's the only source that can
    answer "list every entity of this type" at all; a caller needing the
    authoritative view for one specific row already has
    `entity_{appId}_{entityType}(id)`. Matches `EntityBrowser.vue`'s
    current behavior exactly (its `REPLAY`-fed cache is itself live-fold
    data, not an authoritative overlay) — no regression relative to
    today.
  - One `AccessLogEntry` per **browse query**, not one per row returned
    (a new `"browse"` action, `ResourceRef` = `{appId}:{entityType}` with
    no specific id) — deliberate: `ADR-045` names "every GraphQL query,"
    not "every entity a query happens to touch," and N sequential
    Serializable-isolation hash-chain appends per page load would have
    directly multiplied the exact routine Postgres contention
    `docs/bugs/framework/database/postgres-routine-40001-serialization-
    noise.md` documents (resolved separately, this session).
  - 6 new integration tests, all passing (`EntityQueryHttpSqliteTests.cs`):
    paging slices correctly in `EntityId` order, count reflects the real
    matching set, per-row masking matches the single-entity query exactly,
    the read-claim check is enforced, and the one-AccessLogEntry-not-N
    behavior is verified directly. Full solution build clean.
  - [x] **Client wiring — done.** Design question resolved: neither pure
    (a) nor (b) from the original scoping note — added a real, scoped
    server-side `contains` argument to `entities_{appId}_{entityType}`/
    `entityCount_{appId}_{entityType}` (a plain substring `WHERE` clause
    on `EntityId`, not a full `EventFilterInput`-style multi-clause
    filter — deliberately narrower, matching the one concrete problem
    that motivated the filter box in the first place). New
    `client-web/packages/mvvm-client/src/api/entityQueryBuilder.ts`
    (introspects the `{appId}_{entityType}_Entity` graph type, mirroring
    `subscriptionBuilder.ts`'s already-established pattern for
    Subscription payload types; deliberately excludes masked fields and
    `attachments` from the browse-list projection — Detail view is where
    a caller reviews either) and
    `useEntityBrowserQuery.ts` composable. `EntityBrowser.vue` rewritten
    to fetch real server pages via `n-data-table`'s remote-pagination
    mode instead of listing `useEntityCacheStore`'s accumulated `REPLAY`
    cache (Detail view's own real-time use of that cache is unaffected).
    New unit tests (`entityQueryBuilder.spec.ts`) and a rewritten
    `EntityBrowser.spec.ts` (mocks `fetch` directly, matching
    `EventComposer.spec.ts`'s own established convention, since the data
    source is no longer a seedable Pinia store).
    - **Two real bugs found only by actually running the Playwright
      playbooks, not assumed**: (1) a genuine **race condition** — the
      initial unfiltered page-load (on mount) and the debounced filtered
      reload (300ms after typing) are independent requests with no
      guaranteed resolution order; an unlucky ordering let the stale
      unfiltered response overwrite the correct filtered one after it
      briefly rendered. Fixed with a monotonic request-generation guard
      in `EntityBrowser.vue` (a response is only applied if it's still
      the most recently *started* request, regardless of arrival order).
      (2) All 8 playbooks using the filter box waited only for their
      target row's own visibility before capturing a screenshot — since
      that row could already be on the default unfiltered first page
      (alphabetical luck), the assertion could pass, and the screenshot
      get captured, *before* the debounced filter's fetch had even run.
      Caught by actually reviewing a generated screenshot (filter box
      showing typed text, table still showing the full unfiltered list)
      — fixed all 8 to wait for the table to settle to exactly 1 row
      first, a deterministic signal the filter actually took effect.
    - Full regression green: `dotnet build EventStore.slnx` (0 errors),
      full `client-web` Vitest suite both workspaces (43 + 128 tests),
      full `EventStore.E2ETests` Playwright suite (13/13, run together
      against one live `AppHost`).

- [ ] **Configurable presentation-type view for display elements**
  (direct request, found at the same time as grid pagination above;
  **scope broadened** by the "User Requested Tasks" review below — the
  user's own separate "expected presentation types per object/child set"
  ask is the generalized form of this same gap, not a second thing to
  build in parallel: `ViewDefinition.TemplateContent` is imperative,
  hand-authored HTML+JS per entity type, "never precompiled"
  (`docs/data/schema-registry.md`) — there is no declarative,
  per-field "how should this render" metadata anywhere today, charts
  being only the specific case that was named first). Some fields/views
  (the user named "graphs" — likely this project's own time-series-
  shaped data, e.g. Vitals' IONM alert telemetry, not a literal existing
  "graph" component, since none exists yet) would read better as a
  chart than as a table row; more generally, a field/child-set could
  declare its own presentation type at all (chart, table, badge, ...)
  rather than every `ViewDefinition` re-deciding by hand. Needs: (1) a
  charting library decision — Naive UI ships no charting components of
  its own, so this is a new dependency choice, same "buy over build"/
  "verify before citing" bar as Naive UI itself, with its own
  `docs/libraries/web/{library}.md` entry once chosen; (2) a
  declarative presentation-type config (`chartable`/`chartType`/
  `presentationType` alongside the existing `.columns.js`/`.fields.js`
  ViewModel-structure convention `docs/patterns/mvvm-client-
  architecture.md` already establishes, not a one-off per component) —
  scope this as the general mechanism from the start now that both asks
  are known to be the same gap, not charts-only with presentation-types
  bolted on later; (3) at least one real example wired against real data
  (Vitals' IONM/device telemetry is the natural first candidate, given
  its own time-series shape already discussed in `docs/domains/
  clinical-trials-device-telemetry/`). Deliberately sequenced as
  follow-on work after the Naive UI shell/restyle lands, not folded into
  it blind — picking a charting library and a config schema is its own
  real decision, not a restyle detail.

## User Requested Tasks

Reviewed against the existing codebase/docs (direct request) to check
for duplicates before treating any of these as new work — findings
below each item, refined into what's actually still open.

- [ ] **Extend the entity schema metadata** — reviewed, not a duplicate,
  but split into pieces of very different size/shape:
  - [ ] **Field-level validation and datatype rules** — genuine, real
    gap, not a duplicate: `JsonSchemaInstanceValidator`
    (`src/EventStore.SchemaRegistry/JsonSchemaInstanceValidator.cs`) is
    a **hand-written, intentionally partial** validator (type/required/
    properties/items only — a full JSON Schema library rejects this
    project's own `x-masking` vendor extension, which is why it's
    hand-written at all) with **no** `pattern`/`minLength`/`maxLength`/
    `minimum`/`maximum`/`enum`/`format` support today. Real, scoped
    work: extend that validator (or find/adopt a JSON Schema library
    that can be taught to tolerate `x-masking` alongside real
    validation — check for one before writing more bespoke validation
    code, per this project's own buy-over-build rule) to cover these
    keywords.
  - [ ] **Custom/dependent-field validation (mappings, dependent
    fields)** — genuinely new, confirmed not covered anywhere
    (no `dependentRequired`/`if`-`then`-`else`/cross-field check exists
    in the validator or schema registry today). Needs its own design
    pass — likely an extension of the same validator above once it
    exists, not a separate mechanism.
  - [ ] **Calculated fields** — **do not build as a new mechanism**:
    `ADR-007` (`docs/adrs/adr-007-derived-event-types.md`, Accepted) +
    `DerivationDefinition`/`SelectField`
    (`src/EventStore.Domain/SchemaRegistry/`) already do cross-source
    field **mapping** via `EventStore.Derivation`'s `DerivationWorker`,
    but `SelectField` is a straight 1:1 rename/copy today, with no
    formula/expression/aggregation support. A true calculated field
    (e.g. `Total = Quantity * UnitPrice`) is a real, new capability, but
    belongs as an extension of `ADR-007`'s existing derivation
    mechanism, not a second, parallel one — scope it there.
  - [ ] **Expected presentation types per object/child set** — this is
    the SAME gap as the already-tracked "Configurable presentation-type
    view for display elements" item above (that item's own scope was
    broadened to cover this directly) — not a separate task, don't
    duplicate it here.
- [ ] **Migrate embedded PlantUML diagrams to their own `.puml` files +
  a Docker rendering pipeline to `.svg`** (Low Priority, user's own
  priority marking kept). Reviewed: confirmed no such pipeline exists
  today (no `.puml` file anywhere in the repo, nothing in `client-web/
  package.json`, no CI workflow, no `scripts/` entry — 97 markdown files
  currently embed PlantUML inline via fenced code blocks). **Real
  tension worth deciding explicitly before building, not silently
  overriding**: `docs/references.md`'s own PlantUML/C4-PlantUML entries
  document exactly why every diagram in this repo is hand-styled,
  `!include`-free, and embedded as plain text — avoiding a required
  external-rendering dependency that failed silently and repeatedly
  before (`CLAUDE.md`'s own standing convention bullet on this). A
  Docker-based render-to-`.svg` pipeline doesn't reintroduce `!include`
  itself, but it does reintroduce the class of "diagrams need working
  external infrastructure to render at all" dependency that convention
  was specifically adopted to avoid — weighed against the real,
  legitimate upside (GitHub itself never renders raw PlantUML text
  inline the way it does Mermaid, so pre-rendered `.svg` would make
  every diagram actually visible there). Confirm this tradeoff is
  accepted before scoping the actual build.
- [ ] **Search for a good DSL for user flows, validations, and
  approvals** — reviewed: moved to
  [`docs/10-open-questions.md`](docs/10-open-questions.md) instead of
  staying here, since this is a genuinely undecided fork (the actual
  definition of that file), not decided work with only the doing left.
  See that file's own row for the existing bespoke mechanisms
  (`RequiredSignature`/`authorityDecision`/`ExpectedResponse`) this
  would need to be weighed against.