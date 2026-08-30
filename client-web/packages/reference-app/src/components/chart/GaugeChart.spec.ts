import { describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import GaugeChart from './GaugeChart.vue'

// vue-echarts' own chart initialization/render happens across a couple of
// microtask ticks after mount -- flushPromises alone isn't always enough,
// found by actually running this (an early version of this test read the
// DOM before ECharts had painted anything into it).
async function flushAll(): Promise<void> {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 20))
}

describe('GaugeChart', () => {
  it('renders real, inspectable SVG output under jsdom -- the whole reason ECharts, not Chart.js, was adopted (ADR-100)', async () => {
    const wrapper = mount(GaugeChart, { props: { value: 0.87, label: 'Match confidence' } })
    await flushAll()

    // Not just "a <div> exists" -- a real <svg> with real gauge markup,
    // proving the SVG renderer (not the Canvas default) is actually wired
    // up, and that the exact testability property this ADR's own
    // comparison doc argued for is real, not theoretical.
    const svg = wrapper.find('svg')
    expect(svg.exists()).toBe(true)
    expect(svg.html()).toContain('87%')
  })

  it('clamps an out-of-range value into [0, 1] rather than rendering it raw', async () => {
    const wrapper = mount(GaugeChart, { props: { value: 1.5 } })
    await flushAll()
    expect(wrapper.find('svg').html()).toContain('100%')
  })

  it('exposes an accessible label reflecting the current value', () => {
    const wrapper = mount(GaugeChart, { props: { value: 0.42, label: 'Match confidence' } })
    expect(wrapper.get('[role="img"]').attributes('aria-label')).toBe('Match confidence: 42%')
  })
})
