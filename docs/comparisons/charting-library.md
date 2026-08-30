[← Comparisons index](README.md)

# Which charting library should back a configurable presentation-type view?

**Raised by:** direct request — "some fields/views would read better as a
chart than as a table row" plus "expected presentation types per
object/child set" (`TODO.md`, the "Configurable presentation-type view"
item, scope broadened from an initial charting-only ask). Naive UI
(`ADR-099`) ships no charting components of its own, so this is a new
dependency choice with no prior art in this repo to extend.

## The fork

### Option A — Apache ECharts, via `vue-echarts`

| | |
|---|---|
| **Pros** | Apache-2.0 (library) / MIT (`vue-echarts` wrapper) — both fully permissive, no revenue gate; a genuine first-class **SVG renderer** (not a hack — refactored onto a virtual DOM in v5.3.0, "2-10x" faster since, per the project's own handbook), selectable at `init()` alongside the default Canvas renderer; broad chart-type coverage including gauge/donut charts (exactly what a 0.0-1.0 confidence-score field needs), tree-shakeable modular imports (`echarts/core` + only the chart types/components actually used) |
| **Cons** | Heavier full-bundle footprint than Chart.js if imported unmodularly (mitigated by the modular import path); a broader, more general-purpose API surface than this project's own narrow initial need (a config schema built for it should stay small on purpose, not expose the whole surface) |

### Option B — Chart.js, via `vue-chartjs`

| | |
|---|---|
| **Pros** | MIT, no revenue gate; smaller bundle, simpler API, very widely used, well documented; peer-compatible with Vue 3 (`vue-chartjs@5.3.4`, `chart.js@4.5.1`) |
| **Cons** | **Canvas-only rendering — no SVG option.** This project's own existing test setup already has a documented, accepted gap here: `docs/libraries/web/axe-core.md` records that `jsdom` (this repo's Vitest environment) has no working `HTMLCanvasElement.getContext`, so axe-core's own canvas-touching rule already silently no-ops under Vitest and has to be verified separately via a real headless-Chromium harness instead. Adopting a canvas-only charting library would put every chart component in the exact same position — no genuine Vitest unit-test coverage of chart output without either installing the `canvas` npm package (a real native-binary dependency this repo has avoided so far) or mocking Chart.js out of the test entirely (verifying nothing about what it actually renders) |

### Option C — ApexCharts, via `vue3-apexcharts`

| | |
|---|---|
| **Pros** | SVG-based by default (same testability advantage as ECharts); polished default styling; Vue 3 peer-compatible |
| **Cons** | **Not simply MIT despite how it is often described.** Verified directly against the project's own LICENSE file and terms page: ApexCharts is dual-licensed — MIT/Community License applies only to individuals, non-profits, educational institutions, and organizations under USD $2,000,000 annual revenue; organizations at or above that threshold must purchase a paid license. Duplex is a reusable framework other organizations build products on (`README.md`'s own stated purpose) — a revenue-gated dependency would impose an unpredictable future commercial licensing obligation on downstream adopters the framework itself has no way to control or even detect. Rejected on licensing grounds alone, independent of any technical comparison |

## Recommendation

**Apache ECharts, via `vue-echarts`.** The deciding factor is the SVG
renderer, not raw feature count: it is the only one of the three options
that lets a chart component be genuinely unit-tested under this
project's own existing `jsdom`/Vitest setup, matching (rather than
repeating) the exact canvas-testability gap `axe-core`'s own adoption
already had to work around by falling back to a real browser harness.
ApexCharts shares that same SVG advantage but is disqualified outright
on licensing — a revenue-gated dependency is not an acceptable default
for a framework meant to be built on by arbitrary downstream
organizations. Chart.js remains a reasonable choice for a *consumer*
application that doesn't need jsdom-testable chart output and wants a
smaller bundle, but that trade-off doesn't fit this framework's own
reference-app testing conventions.

Scope the presentation-type config schema narrowly around what's
actually being built now (a `chartable`/`chartType: 'gauge'` marker for
a 0.0-1.0 confidence field) rather than exposing ECharts' full option
surface through it — the library adoption is broad by nature, the
config surface built on top of it doesn't have to be.
