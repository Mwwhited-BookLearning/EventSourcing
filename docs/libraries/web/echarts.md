[← Libraries index](../README.md)

# Apache ECharts (web)

**What it's for:** a full-featured, framework-agnostic charting engine —
line/bar/pie/gauge/scatter and much more, with either a Canvas or an
SVG renderer.

**Why bought, not built:** a charting engine (correct scale/tick
computation, animation, responsive resizing, accessibility) is a huge
surface area with no project-specific value in rebuilding it.

**Why this one, not Chart.js/ApexCharts:** see
[`docs/comparisons/charting-library.md`](../../comparisons/charting-library.md)
for the full comparison. Short version — its genuine SVG renderer (not a
hack; refactored onto a virtual DOM in v5.3.0) is the only option of the
three that lets a chart be unit-tested under this repo's own `jsdom`/
Vitest setup, avoiding the same canvas-testability gap
[`axe-core.md`](axe-core.md) already had to work around a different way;
ApexCharts was disqualified outright on a real, verified revenue-gated
dual license.

## General usage

```js
import * as echarts from 'echarts/core'
import { GaugeChart } from 'echarts/charts'
import { SVGRenderer } from 'echarts/renderers'
echarts.use([GaugeChart, SVGRenderer])
```

```vue
<template>
  <v-chart :option="{ series: [{ type: 'gauge', data: [{ value: matchConfidence }] }] }" />
</template>
```

Modular imports (`echarts/core` + only the chart types/renderers
actually used) keep the bundle to what's needed, not the whole library.

## Where this project uses it

[`ADR-100`](../../adrs/adr-100-configurable-presentation-type-charting.md)
— a gauge chart for Meridian's `MatchConfidence` field in the KYC
Analyst Queue, via the `vue-echarts` Vue wrapper (see that library's own
entry). `SVGRenderer` specifically, not the default Canvas renderer, to
keep the resulting DOM inspectable in Vitest.

## Links

- [echarts.apache.org](https://echarts.apache.org/)
