import { describe, expect, it, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import axe from 'axe-core'
import GenericFallbackView from './components/entity/GenericFallbackView.vue'
import TemplateRenderer from './components/entity/TemplateRenderer.vue'
import FlagRow from './components/entity/FlagRow.vue'
import type { ClientEntityCacheEntry } from './types'

// ADR-073 -- WCAG 2.1 AA baseline for every screen this client renders.
// axe-core runs the real, published ruleset against the ACTUALLY
// rendered DOM (mounted with `attachTo: document.body` so layout/
// visibility checks see a real page context, not a detached fragment) --
// this is the "automated WCAG 2.1 AA conformance check (e.g. axe-core)"
// this item's own exit criteria name, not a hand-rolled approximation.
//
// Honest, verified limit: jsdom has no real `HTMLCanvasElement.
// getContext` implementation, which `color-contrast` needs -- under
// jsdom this rule always lands in `results.incomplete` (impact
// "serious"), never `violations` or `passes`, confirmed by inspecting
// axe's own output directly, not assumed. Rather than silently ignore
// `incomplete` findings altogether (which would also hide a REAL
// regression in some other rule axe can't fully auto-determine), this
// asserts incomplete is EMPTY BESIDES `color-contrast` specifically --
// and color-contrast itself was independently verified this session
// using a real Chromium rendering engine (Edge headless, `docs/changes/
// 2026-08-11.md` has the full result) against these exact same
// components' real rendered HTML+CSS, finding zero violations and zero
// incomplete findings there too. That one-time real-browser check is
// not itself wired into this automated suite (no headless browser
// dependency exists in this project yet) -- re-run it by hand if
// FlagRow/GenericFallbackView/TemplateRenderer's own colors ever change.
async function expectNoSeriousViolations(wrapper: VueWrapper): Promise<void> {
  const results = await axe.run(wrapper.element as unknown as Element, {
    // Matches WCAG 2.1 AA specifically, per ADR-073's own cited legal
    // baseline (not the newer 2.2 tags, which axe-core's ruleset also
    // supports but this ADR states as "where practical," not the bar
    // this check enforces).
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
  })
  const serious = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')
  expect(serious, JSON.stringify(serious, null, 2)).toHaveLength(0)

  const unexpectedIncomplete = results.incomplete.filter((v) => v.id !== 'color-contrast')
  expect(unexpectedIncomplete, JSON.stringify(unexpectedIncomplete, null, 2)).toHaveLength(0)
}

function makeEntry(overrides: Partial<ClientEntityCacheEntry> = {}): ClientEntityCacheEntry {
  return {
    entityId: 'mvvm-demo:shipment:s-1',
    instanceId: 'instance-a',
    entityType: 'shipment',
    data: { carrier: 'UPS', trackingNumber: '1Z999AA10123456784' },
    extensions: {},
    schemaVersion: 1,
    conflictFlag: false,
    lateArrivalFlag: false,
    authorityStatus: 'accepted',
    cachedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('WCAG 2.1 AA conformance (ADR-073) -- axe-core against the real rendered DOM', () => {
  let mounted: VueWrapper[] = []

  afterEach(() => {
    mounted.forEach((w) => w.unmount())
    mounted = []
  })

  it('the generic property-list fallback view has zero critical/serious violations', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry() }, attachTo: document.body })
    mounted.push(wrapper)
    await expectNoSeriousViolations(wrapper)
  })

  it('the generic fallback view with an Extensions-sourced property still conforms', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry({ extensions: { promoCode: 'SPRING24' } }) }, attachTo: document.body })
    mounted.push(wrapper)
    await expectNoSeriousViolations(wrapper)
  })

  it('a ViewDefinition-template-backed screen has zero critical/serious violations', async () => {
    const wrapper = mount(TemplateRenderer, {
      props: {
        templateContent: '<dl><dt>Carrier</dt><dd>{{ carrier }}</dd></dl>',
        entry: makeEntry(),
      },
      attachTo: document.body,
    })
    mounted.push(wrapper)
    await expectNoSeriousViolations(wrapper)
  })

  it('the shared FlagRow convention has zero critical/serious violations, including its active (warning) state', async () => {
    const wrapper = mount(FlagRow, {
      props: { conflictFlag: true, lateArrivalFlag: false, authorityStatus: 'pending_review' },
      attachTo: document.body,
    })
    mounted.push(wrapper)
    await expectNoSeriousViolations(wrapper)
  })
})
