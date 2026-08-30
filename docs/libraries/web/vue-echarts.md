[← Libraries index](../README.md)

# vue-echarts (web)

**What it's for:** the official Vue 3 component wrapper around Apache
ECharts (`<v-chart>`) — reactive `option`/`theme`/renderer props,
correct resize/dispose lifecycle tied to the host component.

**Why bought, not built:** ECharts' own imperative `init()`/`setOption()`/
`dispose()` lifecycle needs to be wired to Vue's own component lifecycle
correctly (resize observers, teardown on unmount) — a solved problem,
not worth re-solving per component that needs a chart.

## General usage

```vue
<script setup>
import { use } from 'echarts/core'
import { GaugeChart } from 'echarts/charts'
import { SVGRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'
use([GaugeChart, SVGRenderer])
</script>

<template>
  <v-chart :option="option" autoresize />
</template>
```

## Where this project uses it

[`ADR-100`](../../adrs/adr-100-configurable-presentation-type-charting.md)
— the `<v-chart>` component backing the new gauge cell for Meridian's
`MatchConfidence` field in the KYC Analyst Queue (see
[`echarts.md`](echarts.md) for the underlying charting engine itself).

## Links

- [github.com/ecomfe/vue-echarts](https://github.com/ecomfe/vue-echarts)
