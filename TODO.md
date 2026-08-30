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

- [x] **Configurable presentation-type view for display elements** —
  done. `ADR-100` (`docs/adrs/adr-100-configurable-presentation-type-
  charting.md`), gated by
  [`docs/comparisons/charting-library.md`](docs/comparisons/charting-library.md)'s
  three-way comparison (Apache ECharts vs. Chart.js vs. ApexCharts).
  ApexCharts disqualified outright on a real, verified dual license
  (MIT only under USD $2M org revenue — an unacceptable dependency for
  a framework other organizations build products on); ECharts won over
  Chart.js specifically because its genuine SVG renderer is the only
  option of the three that's actually unit-testable under this repo's
  own `jsdom`/Vitest setup, the same canvas gap `axe-core.md` already
  documents.
  - New `GaugeChart.vue` (`client-web/packages/reference-app/src/
    components/chart/`), a narrow `chartable`/`chartType: 'gauge'`
    config on `AuthorityQueue.vue` (kept out of that component's own
    field knowledge, the same discipline every other domain specific
    already gets — the actual field name lives in
    `MeridianAnalystQueue.vue`'s own wrapper), wired to Meridian's real
    `matchConfidence` field in the KYC Analyst Queue.
  - **Three real bugs found only by actually running things**: (1)
    `vue-echarts`' own `autoresize` needs `ResizeObserver`, which jsdom
    doesn't implement — an unhandled promise rejection the first time a
    chart was ever mounted in a Vitest spec; fixed with a no-op stub in
    `vitest.setup.ts`. (2) The gauge's own animated detail text read
    "0%" for about a second regardless of the real value (`valueAnimation:
    true` counts up from zero) — made an early version of this
    component's own regression test flaky; fixed to `false`, which is
    also better UX for a reviewer scanning many queue rows at once. (3)
    A real Playwright screenshot (not the Vitest specs, which never
    exercise actual CSS layout) showed the gauge rendering as an
    illegible, overlapping starburst detached from its own table cell —
    two compounding causes: `vue-echarts`' internal `position: absolute`
    root element escaping to the nearest positioned ancestor with no
    `position: relative` on its own container, and ECharts' own default
    11 tick-mark axis labels (`0, 0.1, 0.2, ... 1`) plus an internal
    title cluttering an 80×80px cell. Fixed both — a real, visually
    confirmed clean gauge, re-verified against a live `AppHost`.
  - New tests: `GaugeChart.spec.ts` (3, asserting real rendered SVG
    content, not just "a component exists") and a new
    `AuthorityQueue.spec.ts` case (the configured field renders as a
    chart, excluded from the plain-text summary). Full regression green:
    `client-web` Vitest both workspaces (47 + 128), full 13-test
    Playwright suite against a live `AppHost`.
  - Deliberately NOT solved here (named in `ADR-100`'s own Consequences,
    not silently dropped): a real multi-point time-series chart for
    Vitals' IONM/device telemetry — that data lives in a different
    subsystem (`TelemetrySample`, `EventStore.Streaming`) than the
    entity-property GraphQL surface this config hooks into, and needs
    its own real data-fetching design, not an accidental extension of
    this pass's narrow gauge scope. `LivenessConfidence`
    (`BiometricCaptureRecorded`) is a second, equally fitting gauge
    candidate, also not wired up this pass — no dedicated queue-style UI
    exists for it today, only `GenericFallbackView`'s generic property
    table, which doesn't yet consult `chartable` config for arbitrary
    schemas.

## User Requested Tasks

Reviewed against the existing codebase/docs (direct request) to check
for duplicates before treating any of these as new work — findings
below each item, refined into what's actually still open.

- [x] **Extend the entity schema metadata** — reviewed, not a duplicate,
  but split into pieces of very different size/shape:
  - [x] **Field-level validation and datatype rules** — done.
    `JsonSchemaInstanceValidator` extended in place (kept hand-written,
    not switched to a library — the same `x-masking`-tolerance reasoning
    that made it hand-written still applies, and adding keywords is
    purely additive to that): `minLength`/`maxLength`/`pattern` (string),
    `minimum`/`maximum`/`exclusiveMinimum`/`exclusiveMaximum` (number),
    `enum` (any type), and a small, real-library-backed `format` subset
    (`date-time` via `DateTimeOffset.TryParse`, `email` via
    `MailAddress.TryCreate`, `uri` via `Uri.TryCreate` — buy over build,
    not a bespoke format-regex vocabulary; an unrecognized format name is
    tolerated, matching `MatchesType`'s own existing "don't fail closed
    on our own uncertainty" posture for an unrecognized `type`).
    - **A real, found-before-shipping compatibility conflict, not
      assumed away**: `ADR-038`'s own `x-enum-fallback` contract ("every
      enum-like field... the raw string travels through unmodified,
      never substituted or dropped") means an out-of-list enum value is
      the *expected* forward-compatibility case that flag exists for,
      not a real violation — a real, already-existing integration test
      (`CompatibilityGraphQlHttpSqliteTests`) publishes exactly this
      scenario. The new `enum` check honors `x-enum-fallback: true` as
      an exemption, the same "vendor extension flag changes validation
      behavior for this field" shape the pre-existing `x-masking`
      exemption already uses — found by grepping this repo's own real
      schemas for the new keywords before shipping, not by a test
      failure after the fact.
    - 22 new unit tests (`JsonSchemaInstanceValidatorTests.cs`,
      `EventStore.UnitTests`), all passing; full `EventStore.
      IntegrationTests` (244/244, including the `ADR-038` scenario
      directly) and full `EventStore.UnitTests` (39/39) green; full
      solution build clean. No existing proving-ground schema (Vitals/
      Meridian/demo) uses any of these keywords yet, confirmed by
      search — purely additive, no behavior change for anything already
      registered.
  - [x] **Custom/dependent-field validation (mappings, dependent
    fields)** — done, as an extension of `JsonSchemaInstanceValidator`
    (the previous sub-item), not a separate mechanism. Real, standard
    JSON Schema keywords (Draft 2019-09+), verified against the spec
    before writing, not bespoke syntax: `dependentRequired` (presence-
    only dependency — "if X is present, Y must be too") and
    `if`/`then`/`else` (the general conditional case — Y's own shape/
    range depends on X's *value*, not just its presence). `const`
    (single-value `enum`) added alongside them — needed for `if`'s own
    realistic test cases ("if `seriousAdverseEvent` is exactly `true`
    ..."), a natural, small, real keyword rather than forcing a
    one-element `enum` array. `if`'s own failures never leak into the
    reported error list (a pure boolean test, evaluated into a throwaway
    list) — only whichever of `then`/`else` actually applies contributes
    real errors. 8 new unit tests; no existing proving-ground schema
    uses any of these keywords yet, confirmed by search. Full
    `EventStore.UnitTests` (47/47) and `EventStore.IntegrationTests`
    (244/244) green, full solution build clean.
  - [x] **Calculated fields** — done, as an extension of `ADR-007`'s
    existing derivation mechanism (`DerivationDefinition`/`SelectField`,
    `EventStore.Derivation`'s `DerivationWorker`), not a second, parallel
    one. `SelectField` now carries a mutually-exclusive `Expression`
    alongside the pre-existing `SourceType`/`SourceField` straight-mapping
    pair; `$select`'s grammar gained a second entry form,
    `"output:=expression"` (`SelectClauseParser`, comma-splitting made
    paren/bracket/string-literal aware so a function-call expression's own
    commas don't get cut). The expression is evaluated through the
    already-registered `IUpcastExpressionEvaluator` (`ADR-053`) — the same
    seam `UpcastFromPrevious` already uses, reused rather than building a
    third expression mechanism, per this project's own buy-over-build
    convention (verified by reading `IUpcastExpressionEvaluator`/
    `CelUpcastExpressionEvaluator`/`JsonataUpcastExpressionEvaluator`
    before designing anything). `"event"` binds to an object keyed by
    each declared source's own lowercased name, so an expression can read
    any arrived source's fields. Compiled (not just evaluated) at
    registration time via `TryCompile`, so a malformed expression is a
    `400` at registration, matching `ADR-018`'s posture for
    `UpcastFromPrevious` itself. `DerivationRegistrationService` and
    `DerivationWorker` (`RunOnceAsync` down through `BuildOutputPayload`)
    both thread the evaluator through; `RunOnceAsync`'s own new parameter
    is optional (default `null`) specifically so the other ~24 existing
    call sites across `EventStore.IntegrationTests` (none of which use a
    calculated field) keep compiling unchanged — a derivation that
    actually needs it with none supplied fails loudly, not silently.
    Real, found-by-running gotcha, documented rather than papered over:
    CEL has no implicit `int`/`double` coercion, so a whole-number JSON
    field multiplied against a decimal field needs an explicit
    `double(...)` cast in the expression. Doc corrections landed in the
    same pass: `docs/data/schema-registry.md`'s `SelectField` shape, and
    an additive "Implementation note" on `ADR-007` itself (its own
    additive-history convention, not a silent rewrite). Two new
    integration scenarios (`DerivationScenarioAssertions`, run against
    all three providers): a calculated field evaluating correctly end to
    end, and an uncompilable expression rejected at registration. Full
    `EventStore.UnitTests` (47/47) and the full SQLite
    `EventStore.IntegrationTests` run green (one unrelated,
    already-pre-existing flaky test outside this area, confirmed to pass
    in isolation); full solution build clean.
  - [x] **Expected presentation types per object/child set** — this is
    the SAME gap as the already-tracked "Configurable presentation-type
    view for display elements" item above (that item's own scope was
    broadened to cover this directly), which is done — not a separate
    task.
- [x] **Migrate embedded PlantUML diagrams to their own `.puml` files +
  a Docker rendering pipeline to `.svg`** (Low Priority, user's own
  priority marking kept). The tradeoff this item's own text flagged
  (reintroducing the class of external-render dependency `docs/
  references.md`'s PlantUML/C4-PlantUML entries document deliberately
  avoiding) was put to the user explicitly and accepted before any of
  this was built.

  `scripts/extract-diagrams.mjs` walks every `docs/**/*.md` file and, for
  each ` ```plantuml`/` ```puml` fenced block, writes its content to its
  own `.puml` file under `docs/diagrams/` (mirroring the source doc's own
  relative path — e.g. `docs/features/auth.md`'s diagrams live under
  `docs/diagrams/features/auth/`), then inserts a `![...](....svg)`
  image reference immediately above the original fence. **The original
  fenced block is left completely untouched** — deliberately, not an
  oversight: this repo's docs are meant to stay readable as plain text
  with no external tool (the same reasoning behind the `!include` ban),
  so the inline PlantUML stays the visible, git-diffable source of
  truth; the `.puml` copy is only the render pipeline's input.
  Idempotent/re-runnable (a guard skips re-inserting the image line for
  an already-migrated fence — this had a real bug on the first pass,
  caught before committing: the guard checked the wrong line, since a
  blank line separates the image reference from the fence, and doubled
  every image line on a second run until fixed to skip blank lines when
  looking back).

  `scripts/render-diagrams.mjs` renders every `docs/diagrams/**/*.puml`
  to a sibling `.svg` via the official `plantuml/plantuml` Docker image
  (`docker run ... plantuml/plantuml -tsvg "/workdir/docs/diagrams/**/*.puml"`
  — the directory-only form silently renders nothing, since PlantUML
  only recurses through a `**` glob, not a bare directory path). Each
  extracted `.puml`'s `@startuml`/`@startsalt` line has its optional
  `Name` argument stripped (only in the `.puml` copy, never in the
  inline fence) specifically so PlantUML falls back to naming its output
  after the input file's own basename — otherwise it names the `.svg`
  after the diagram's internal title, which is unpredictable and would
  silently break the image reference above.

  **Six genuine, previously-undiscovered PlantUML syntax bugs found only
  by actually running every diagram through a real renderer** — this
  project's own recurring finding, that a real run surfaces bugs no
  amount of reading the source catches — fixed at their source doc (or,
  for one E2E-test-generated playbook doc, at the owning test's own step
  caption, per that file's own "don't hand-edit, the next run overwrites
  it" instruction):
  - Three participant/actor labels used a backslash-escaped quote inside
    an already-double-quoted label (`"...key \"Fhir\"..."`) — PlantUML
    has no such escape; fixed by switching the inner quotes to single
    quotes (`'Fhir'`). `docs/features/bulk-ingestion-and-interchange-
    adapters.md` (two), `tests/EventStore.E2ETests/
    VitalsWorkflowBAdverseEventPlaybookTests.cs` (the source of the
    generated playbook doc showing the same pattern).
  - `docs/features/auth.md`'s RBAC sequence diagram used `note over A, B,
    C` (three participants) — this specific PlantUML build
    (`1.2026.7`) errors on a 3+-name `note over` list; fixed to the
    idiomatic two-endpoint form (`note over A, C`), which already spans
    every lifeline between them visually.
  - `docs/playbooks/meridian/README.md`'s object diagram declared an
    `actor` (a sequence-diagram element) alongside plain `object`
    declarations — fixed to `object`, matching the rest of the diagram.
  - `docs/domains/clinical-trials-device-telemetry/features/trial-data-
    export-and-subject-rights.md`'s Salt mockup nested literal `{ }`
    inside an already-`{ }`-delimited Salt cell's own quoted string
    (`"{ erased: true }"`) — Salt's brace-based grammar doesn't respect
    quoting for its own structural braces, crashing the renderer with a
    raw Java `StringIndexOutOfBoundsException`, not a normal "line N"
    diagram error; fixed by dropping the nested braces (`"[erased:
    true]"`).
  - `docs/mental-vision.md` (an orphaned, unreferenced early scratch doc
    predating this repo's current doc taxonomy — not part of `CLAUDE.md`'s
    documented layout) was missing `@startuml`/`@enduml` entirely; added,
    since it was otherwise the one broken link in an else-complete
    migration.

  All 333 diagrams across 96 files (240 `@startuml`, 92 `@startsalt`) now
  render cleanly, verified via a full `node scripts/render-diagrams.mjs`
  run exiting 0 with zero `Error line` output. Not wired into CI (not
  asked for this pass) — re-run both scripts after editing any diagram to
  keep the checked-in `.svg` files in sync; this is a real, honest manual
  step, not automated yet.

(The "DSL for user flows/validations/approvals" ask was moved to
[`docs/10-open-questions.md`](docs/10-open-questions.md) row 1, not kept
here — a genuinely undecided fork, not decided work with only the doing
left.)

- [ ] **Write `docs/patterns/fault-injection-chaos-engineering.md`** —
  found while fixing `docs/bugs/framework/service/follow-client-faults-
  under-default-http-resilience-timeout.md`: `docs/references.md`'s own
  "Chaos Engineering" row has pointed at this file since `ADR-063`, but
  the file itself was never actually written — a pre-existing dangling
  reference, not introduced this pass. Matches `docs/patterns/README.md`'s
  own catalog-entry convention (general pattern first, cited, then which
  ADR applies it here — `ADR-063`, via the now-hand-rolled `FaultInjector`
  rather than `Polly`+`Simmy`, see `docs/libraries/dotnet/polly-simmy.md`).