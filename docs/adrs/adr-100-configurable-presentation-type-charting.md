[← ADR index](../07-adrs.md)

# ADR-100: Adopt Apache ECharts and a declarative field-level presentation-type config

Status: Accepted

Context: Direct request — "some fields/views would read better as a
chart than as a table row," broadened (found while scoping the same
item) to a more general ask: "expected presentation types per
object/child set." `ViewDefinition.TemplateContent` (`ADR-039`) is
imperative, hand-authored HTML+JS per entity type, "never precompiled"
(`docs/data/schema-registry.md`) — there is no declarative, per-field
"how should this render" metadata anywhere in this design today. Naive
UI (`ADR-099`) ships no charting components of its own, so adopting a
charting library is a genuinely new dependency decision, not an
extension of an existing one.

Decision:
- Adopt **Apache ECharts**, via the `vue-echarts` wrapper, per
  [`docs/comparisons/charting-library.md`](../comparisons/charting-library.md)'s
  full comparison against Chart.js and ApexCharts. The deciding factor:
  ECharts' genuine SVG renderer is the only one of the three options
  that lets a chart component be unit-tested under this repo's own
  existing `jsdom`/Vitest setup, the same canvas-testability gap
  `docs/libraries/web/axe-core.md` already documents and works around by
  falling back to a real browser harness — adopting a second,
  canvas-only dependency would repeat that same gap rather than avoid
  it. ApexCharts is disqualified outright: verified directly against its
  own LICENSE file, it is dual-licensed (MIT only under USD $2,000,000
  annual organization revenue), an unacceptable dependency for a
  framework other organizations build products on.
- A new, narrow **declarative presentation-type config**: a
  `chartable`/`chartType` marker alongside the existing `.columns.js`/
  `.fields.js` ViewModel-structure convention
  (`docs/patterns/mvvm-client-architecture.md`), not a one-off per
  component and not an attempt to expose ECharts' full option surface
  through it. This ADR ships exactly one chart type (`chartType:
  'gauge'`, for a 0.0–1.0 confidence-shaped numeric field) — a second
  chart type is real, separate work when a real field actually needs
  one, not spec'd speculatively here.
- **First real example**: Meridian's `MatchConfidence`
  (`SanctionsScreeningPerformed`, `src/Samples.Meridian/
  MeridianWorkflowC.cs`) — a genuine 0.0–1.0 numeric field already
  surfaced in a real, already-built UI (`AuthorityQueue.vue`'s own KYC
  Analyst Queue). A gauge chart replacing the plain decimal in that
  queue's own summary column is the concrete, real change this ADR
  ships. `LivenessConfidence` (`BiometricCaptureRecorded`,
  `MeridianWorkflowA.cs`) is an equally fitting second candidate, noted
  here rather than silently forgotten, but not wired up this pass — it
  has no dedicated queue-style UI today, only `GenericFallbackView`'s
  generic property table, which doesn't yet know how to consult this
  per-field config at all (a real, separate follow-on: extending
  `GenericFallbackView` to check `chartable` config for arbitrary
  schemas, not just a component with its own hardcoded column list).

Consequences:
- `docs/libraries/web/echarts.md` and `docs/libraries/web/vue-echarts.md`
  added, `docs/libraries/README.md` gets both rows.
- A new `ChartCell.vue` (or equivalently named) component in
  `client-web/packages/reference-app` renders a gauge for any column
  configured `chartable: { chartType: 'gauge' }`; `AuthorityQueue.vue`'s
  own column definitions gain this marker for `MatchConfidence`/
  `LivenessConfidence` specifically, not for every numeric field
  generically — a blanket "every number becomes a chart" rule was
  considered and rejected as worse UX for genuinely tabular numeric data
  (e.g. `SequenceNumber`, counts).
- Deliberately NOT solved here: a chart type for genuine multi-point
  time-series data (Vitals' IONM/device telemetry, the TODO item's own
  originally-named example) — that data lives in a different subsystem
  (`TelemetrySample`, raw-bytes-per-sample, queried via
  `EventStore.Streaming`, not the entity-property GraphQL surface this
  config hooks into) and would need its own real data-fetching design
  (most plausibly composing multiple `playbackAsOf` calls across a
  SequenceNumber range, or a dedicated telemetry-series query), not
  something this ADR's narrow gauge-chart scope can absorb by accident.
  Tracked as a named follow-on in `TODO.md`, not silently dropped.
