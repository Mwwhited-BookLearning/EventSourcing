[← ADR index](../07-adrs.md)

# ADR-099: Naive UI + Vue Router left-hand-nav shell for `client-web`, with a paged-grid and chart-view extensibility point

Status: Accepted

Context: `client-web`'s reference app (`ADR-039`) has grown, this
session, from one generic entity view into eight real tabs (Detail,
Browse, Composer, two domain Queues, Relying-Party Access, Lineage &
Playback) across twelve Playwright-verified playbooks
(`docs/playbooks/README.md`). The shell that hosts them is still exactly
what it was at `ADR-039`'s original scope: a plain top-nav row of HTML
`<button>` elements switching a single `activeTab` ref
(`App.vue`), styled with no shared component library or design-token
system. `ADR-039` itself never addresses navigation pattern or routing
at all — the "plain tab switcher, not a router dependency" note that had
been informally attributed to it in this session's own `TODO.md` is
actually a code comment in `App.vue` citing `08-build-plan.md`'s
"Proving-Ground Application UX" item, not any ADR text; that
misattribution is corrected here, not carried forward. Direct request,
this session: adopt Naive UI (`naiveui.com`) for component styling, and
a left-hand-navigation shell "similar to Azure Portal and Azure DevOps."
Two more concrete needs surfaced while starting that work: (1) every
grid-shaped view (`EntityBrowser.vue`, both domain Queue components)
renders every row of an already-fully-loaded client-side cache with no
paging at all; (2) some data would read better as a chart than as a
table row, and there should be a declared, config-driven way to say so
per field/view rather than a one-off per component.

`docs/libraries/README.md` already carries a Naive UI catalog row
(citing `docs/patterns/mvvm-client-architecture.md`'s **Styling** layer
— one `theme/tokens.js` config flowing through `themeOverrides`) written
during that pattern doc's own authoring, before this session's `npm
install` ever ran. That row is accurate for what it describes (the
theming layer) but was aspirational until now — no component actually
imported Naive UI before this ADR's implementation. It is not
contradicted by this decision, only completed; its "Adopted in" column
is updated to point here since this is the ADR that actually landed it.

Decision:
- **Adopt Naive UI** (`naive-ui@2.45.3`, Vue 3 peer, npm audit clean) as
  `client-web`'s component library, exactly as `docs/patterns/mvvm-
  client-architecture.md`'s Styling layer already specified: one
  `n-config-provider` at the app root carrying `theme/tokens.js`'s
  `themeOverrides`, no component overriding its own colors/spacing
  locally.
- **Adopt Vue Router** (`vue-router@5.3.0`, peer-compatible with this
  repo's Vue 3.5.41) to replace the single-`ref` tab switcher with real
  routes — one route per existing tab (`/detail`, `/browse`, `/compose`,
  `/queue`, `/relying-party`, `/lineage`), domain-gated routes (the two
  Queue routes) filtered the same way `App.vue`'s existing `queueDomain`
  computed already gates them, just expressed as router `meta` +
  navigation guard instead of a template `v-if`.
- **Left-hand navigation rail**, Azure Portal/DevOps-style: `n-layout`
  with a collapsible `n-layout-sider` hosting `n-menu` (route-linked
  items, `router-link`-rendered), replacing the top tab-button row
  entirely. Collapsed state is a per-viewer UI convenience
  (`localStorage`, not app state).
- **Every existing component gets restyled with real Naive UI
  components in the same pass as the shell**, not deferred component-by-
  component (explicit user choice over the phased alternative):
  `EntityView`/`GenericFallbackView` (`n-descriptions`), `EntityBrowser`/
  the two Queue components (`n-data-table`), `EventComposer` (`n-form`),
  `RelyingPartyAccessPanel`/`LineageExportAndPlaybackPanel`/
  `BitemporalPlaybackControl`/`OfflineBundleViewer` (`n-card`/`n-steps`/
  `n-button` as fits each).
- **Grids use `n-data-table`'s built-in pagination** against their
  existing in-memory arrays. This is explicitly a partial fix, recorded
  as such rather than overclaimed: `EntityBrowser`/the Queue components
  are backed by `useEntityCacheStore`, filled by a `mode: REPLAY` GraphQL
  *subscription* tail (`ADR-039`) that already streams every matching
  event to the client before any grid renders a row — paginating the
  rendered table bounds DOM cost, but does not reduce what crosses the
  wire. A genuine paged, cursor-based entity-list *query* (as an
  alternative data source to always-subscribe `REPLAY` mode) is real,
  separate server+schema work, tracked in `TODO.md`, not designed here —
  this ADR commits only to the render-side pagination and to the shape
  of the future gap, not to a query design not yet scoped.
- **A chart-view configuration point, extensibility only — no charting
  library adopted here.** Naive UI ships no chart components; picking
  one (candidates to evaluate: `vue-echarts`/ECharts, `vue-chartjs`/
  Chart.js) is a real "buy over build" decision this ADR defers rather
  than makes blind, per this project's own verify-before-citing rule.
  What this ADR does commit to now, so the restyle pass doesn't have to
  be revisited later: the existing `.columns.js`/`.fields.js` ViewModel-
  structure convention (`docs/patterns/mvvm-client-architecture.md`)
  gains an optional `chartable: { type, xField, yField }`-shaped
  declaration point per field/view; until a library is chosen, any
  `chartable` declaration renders as its current table/description row
  unchanged (a no-op today, a real hook once `TODO.md`'s follow-on item
  lands).

Consequences:
- `docs/07-adrs.md` gets this ADR's index row.
- `docs/libraries/README.md`'s Naive UI row's "Adopted in" column
  updates to cite this ADR (additive: the row's existing description of
  the theming layer stays correct and unchanged); a new Vue Router row
  is added the same pass.
- `App.vue` changes from a tab-`ref` switcher to a router-driven shell;
  every Vitest component spec whose selectors assumed plain HTML markup
  needs updating to the Naive UI-rendered equivalent, in the same pass
  this ADR's implementation lands, not deferred.
- Every one of the 12 Playwright playbooks' own navigation steps
  (`GetByRole(AriaRole.Button, new() { Name = "Browse" })`-style top-tab
  clicks) needs updating to the new left-nav markup and re-verifying
  together against a live `AppHost` before this item is marked done in
  `TODO.md`.
- Deliberately NOT solved here: (1) the real paged/cursor entity-list
  GraphQL query (server+schema work, separately tracked); (2) which
  charting library to adopt and its own `docs/libraries/web/*.md` entry
  (separately tracked); (3) `style-guide.md` (already sequenced in
  `TODO.md` to start only after this ADR's shell/restyle work is done,
  so it documents the real target UI, not a moving one).

