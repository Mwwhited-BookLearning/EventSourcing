import { afterEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { virtual } from '@guidepup/virtual-screen-reader'
import GenericFallbackView from './components/entity/GenericFallbackView.vue'
import TemplateRenderer from './components/entity/TemplateRenderer.vue'
import type { ClientEntityCacheEntry } from '@eventstore/mvvm-client'

// ADR-073/build-plan item 45's own exit criterion asks for "a manual
// screen-reader pass" (a real NVDA/JAWS/VoiceOver session) confirming
// GenericFallbackView is fully navigable, not merely visually present --
// TODO.md's own honestly-named gap, since no such software is
// installable/operable in this environment. `@guidepup/virtual-screen-
// reader` narrows that gap without literally closing it: a pure-JS
// simulator that walks a real DOM's accessibility tree and produces the
// same kind of ordered "what would be announced" phrase log a real
// screen reader would, with no OS-level screen-reader engine involved at
// all -- confirmed working here, not assumed from its own docs. Its own
// README is explicit that it "should not be used as a substitute for
// testing with real screen readers and with real screen reader users,"
// a limit `axe-core`'s own a11y.spec.ts (WCAG rule conformance, not
// navigation order) can't even approach: axe-core cannot tell whether a
// table's first column is semantically a label, which is exactly the gap
// this file closes concretely instead of by reasoning about markup by
// hand (this component's own `<th scope="row">`/`<caption>` fix was
// originally justified that way, before this tool existed in this repo).
//
// One further, real limit found while writing this, not glossed over:
// the log below shows FlagRow's own "⚠" glyph is carried through as
// literal text ("⚠ ConflictFlag"), not translated into any spoken
// description -- this tool proves the CONTENT reaches the accessibility
// tree and is reachable in the right place, but real screen readers
// genuinely differ (and have documented inconsistencies) in how they
// pronounce a bare Unicode symbol character, something only a real
// NVDA/JAWS/VoiceOver session could actually confirm one way or the
// other. Named here rather than claimed proven.
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

// Walks the ENTIRE simulated accessibility tree rooted at document.body,
// not just the mounted component's own root -- the same "real page
// context, not a detached fragment" reasoning a11y.spec.ts's own
// `attachTo: document.body` mounting already established for axe-core.
async function readEntireDocument(): Promise<string[]> {
  await virtual.start({ container: document.body })
  while ((await virtual.lastSpokenPhrase()) !== 'end of document') {
    await virtual.next()
  }
  const log = await virtual.spokenPhraseLog()
  await virtual.stop()
  return log
}

function assertOrdered(log: string[], ...phrasesInOrder: string[]): void {
  let lastIndex = -1
  for (const phrase of phrasesInOrder) {
    const index = log.indexOf(phrase, lastIndex + 1)
    expect(index, `expected "${phrase}" after index ${lastIndex} in: ${JSON.stringify(log, null, 2)}`).toBeGreaterThan(lastIndex)
    lastIndex = index
  }
}

describe('Screen-reader navigability (ADR-073/build-plan item 45) -- @guidepup/virtual-screen-reader over the real rendered DOM', () => {
  let mounted: VueWrapper[] = []

  afterEach(() => {
    mounted.forEach((w) => w.unmount())
    mounted = []
  })

  it('the generic fallback table announces each property\'s label and value together, not as two anonymous cells', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry() }, attachTo: document.body })
    mounted.push(wrapper)
    const log = await readEntireDocument()

    // The hidden <caption> IS announced (a screen reader reads a
    // visually-hidden-but-not-aria-hidden caption, unlike a sighted user).
    expect(log).toContain('caption, Entity properties')

    // The actual claim the original `<th scope="row">` fix made, proven
    // rather than reasoned about: "carrier"/"UPS" are announced as one
    // grouped row ("row, carrier UPS"), with the label surfaced as a
    // distinct "rowheader" role (not a second anonymous "cell") ahead of
    // its value within that same row.
    assertOrdered(log, 'row, carrier UPS', 'rowheader, carrier', 'cell, UPS', 'end of row, carrier UPS')
    assertOrdered(log, 'row, trackingNumber 1Z999AA10123456784', 'rowheader, trackingNumber', 'cell, 1Z999AA10123456784')
  })

  it('an Extensions-sourced property\'s own visual "(Extensions)" marker is actually conveyed, not just visually styled', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry({ extensions: { promoCode: 'SPRING24' } }) }, attachTo: document.body })
    mounted.push(wrapper)
    const log = await readEntireDocument()

    assertOrdered(log, 'row, promoCode SPRING24 (Extensions)', 'rowheader, promoCode', 'cell, SPRING24 (Extensions)', 'emphasis', '(Extensions)', 'end of emphasis')
  })

  it('the Retry sync button is reachable and announced with a real accessible name', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry() }, attachTo: document.body })
    mounted.push(wrapper)
    const log = await readEntireDocument()

    expect(log).toContain('button, Retry sync')
  })

  it('a ViewDefinition-template-backed screen\'s <dl> pairs its own term and definition in navigation order', async () => {
    const wrapper = mount(TemplateRenderer, {
      props: { templateContent: '<dl><dt>Carrier</dt><dd>{{ carrier }}</dd></dl>', entry: makeEntry() },
      attachTo: document.body,
    })
    mounted.push(wrapper)
    const log = await readEntireDocument()

    assertOrdered(log, 'term', 'Carrier', 'end of term', 'definition', 'UPS', 'end of definition')
  })

  it('FlagRow\'s active/warning state content reaches the accessibility tree, distinct from its non-active phrasing', async () => {
    const inactive = mount(GenericFallbackView, { props: { entry: makeEntry() }, attachTo: document.body })
    mounted.push(inactive)
    const inactiveLog = await readEntireDocument()
    inactive.unmount()
    mounted = []

    const active = mount(GenericFallbackView, { props: { entry: makeEntry({ conflictFlag: true }) }, attachTo: document.body })
    mounted.push(active)
    const activeLog = await readEntireDocument()

    expect(inactiveLog).toContain('ConflictFlag: false')
    // The literal text content genuinely differs when active -- proven,
    // not assumed, that this reaches the tree in the right place (inside
    // the same "status" region every other flag already lives in).
    // Whether a real screen reader actually PRONOUNCES the leading "⚠"
    // glyph itself is outside what this tool can confirm one way or the
    // other -- named in this file's own header comment, not claimed here.
    expect(activeLog).toContain('⚠ ConflictFlag')
    assertOrdered(activeLog, 'status', '⚠ ConflictFlag', 'LateArrivalFlag: false', 'AuthorityStatus: accepted', 'end of status')
  })
})
