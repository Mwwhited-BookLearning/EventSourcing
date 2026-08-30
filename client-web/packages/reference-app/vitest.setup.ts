// ADR-039's client outbox/entity cache are IndexedDB-backed; jsdom (the
// Vitest test environment below) doesn't implement IndexedDB at all --
// fake-indexeddb is the standard, real-enough-for-tests shim, imported once
// here so every spec file gets a working `indexedDB` global with no
// per-file boilerplate.
import 'fake-indexeddb/auto'

// ADR-100 -- vue-echarts' own `autoresize` prop (GaugeChart.vue) watches
// its container via `ResizeObserver`, which jsdom doesn't implement
// either -- found as a real unhandled promise rejection the first time a
// chart component was actually mounted in a Vitest spec, not assumed.
// A no-op stub is sufficient here: no spec in this repo asserts on
// resize BEHAVIOR, only on a chart's own rendered output, and ECharts'
// own SVG renderer (the entire reason this library was chosen over
// Chart.js, ADR-100) still produces real, inspectable SVG without a
// working resize callback ever firing.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class ResizeObserver {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }
}
