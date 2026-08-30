<script setup lang="ts">
import { computed } from 'vue'
import { use } from 'echarts/core'
import { GaugeChart as EChartsGaugeChart } from 'echarts/charts'
import { SVGRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'

// ADR-100 -- SVGRenderer specifically, not the (default) CanvasRenderer:
// the whole reason this project adopted Apache ECharts over Chart.js was
// its genuine SVG renderer, so a chart's actual output stays inspectable
// in Vitest under jsdom (which has no working HTMLCanvasElement.getContext
// -- the same limitation docs/libraries/web/axe-core.md already documents
// for a different library). Registered once, at this shared component's
// own module scope, not per chart instance.
use([EChartsGaugeChart, SVGRenderer])

// A 0.0-1.0 confidence-shaped value, not an arbitrary range -- this is
// ADR-100's own deliberately narrow first chart type (a confidence-score
// gauge), not a general-purpose gauge for any numeric field. A value
// outside [0, 1] is clamped rather than rendered wrong or thrown on --
// defensive against a future schema allowing e.g. a percentage stored as
// 0-100 by mistake, found worth guarding given this reads directly off a
// caller-supplied payload value with no schema validation at this layer.
const props = defineProps<{
  value: number
  label?: string
}>()

const clampedValue = computed(() => Math.min(1, Math.max(0, props.value)))

const option = computed(() => ({
  series: [
    {
      type: 'gauge',
      min: 0,
      max: 1,
      radius: '90%',
      progress: { show: true, width: 10 },
      axisLine: { lineStyle: { width: 10 } },
      // All off -- a real Playwright screenshot (not the Vitest specs,
      // which never exercise real CSS layout) showed ECharts' own default
      // 11 tick marks/axis-number labels (0, 0.1, 0.2, ... 1) rendering
      // as an illegible, overlapping starburst at this component's actual
      // 80x80px table-cell size. A compact gauge needs just the colored
      // arc and the one center number, not a full labeled dial.
      axisTick: { show: false },
      splitLine: { show: false },
      axisLabel: { show: false },
      pointer: { show: false },
      // ECharts' own gauge renders `data[].name` as an internal title by
      // default -- off, since the column header (AuthorityQueue.vue) is
      // this chart's own label already; showing it a second time, inside
      // an 80px circle, is exactly the illegible-overlap problem above,
      // not a separate one.
      title: { show: false },
      detail: {
        // false, not true -- a queue reviewer scanning many rows at once
        // wants each gauge's own number correct immediately, not counting
        // up from 0% every time the row re-renders. Also what made this
        // component's own regression test unreliable before this fix: the
        // detail text reads "0%" for the first ~1s regardless of the real
        // value, found by actually reading the rendered SVG in a test, not
        // assumed from the value passed in.
        valueAnimation: false,
        formatter: (v: number) => `${Math.round(v * 100)}%`,
        fontSize: 14,
        offsetCenter: [0, 0],
      },
      data: [{ value: clampedValue.value, name: props.label }],
    },
  ],
}))
</script>

<template>
  <v-chart
    class="gauge-chart"
    :option="option"
    :aria-label="label ? `${label}: ${Math.round(clampedValue * 100)}%` : `${Math.round(clampedValue * 100)}%`"
    role="img"
    autoresize
  />
</template>

<style scoped>
.gauge-chart {
  width: 80px;
  height: 80px;
  /* vue-echarts' own root <svg>/<canvas> renders `position: absolute;
     left: 0; top: 0` internally -- with no positioned ancestor here,
     that escapes to whichever ancestor actually IS positioned (or the
     viewport), rendering the gauge detached from its own table cell and
     overlapping unrelated rows/columns. Found only by actually looking
     at a real Playwright screenshot, not from the Vitest specs, which
     never exercise real CSS layout under jsdom. `overflow: hidden` is
     defensive in the same spirit -- the SVG's own reported size can
     exceed this box by a pixel or two depending on font metrics. */
  position: relative;
  overflow: hidden;
}
</style>
