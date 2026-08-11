import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import OfflineBundleViewer from './OfflineBundleViewer.vue'
import { computeManifestHash } from '../../playback/verifyBundle'

// crypto.subtle.digest resolves via a macrotask, not a plain microtask --
// flushPromises() alone (a microtask-queue drain) doesn't wait long enough
// for it, confirmed by direct observation this session rather than
// assumed. A SINGLE setTimeout(0) tick was enough in isolation but proved
// flaky once the full suite's own concurrency put more load on the event
// loop (caught by a real, if intermittent, failure this session, not
// hypothesized) -- several ticks with a short real delay is the more
// robust wait.
async function flushAll(): Promise<void> {
  await flushPromises()
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 5))
}

async function makeNdjson(payloads: string[]): Promise<string> {
  const events = payloads.map((payload, i) => ({
    eventId: `event-${i}`,
    appId: 'lab1',
    entityId: 'lab1:Evidence:ev-1',
    eventType: 'ArtifactExtracted',
    schemaVersion: 1,
    sequenceNumber: i + 1,
    chainHash: `chain-${i}`,
    payloadHash: `hash-${i}`,
    payload,
    occurredAt: '2026-08-11T10:00:00-04:00',
    lateArrivalFlag: false,
  }))
  const manifestHash = await computeManifestHash(events.map((e) => e.chainHash), 'auditor-3', '2026-08-11T10:30:00.123-04:00')
  const manifest = {
    entityId: 'lab1:Evidence:ev-1',
    eventTypeDefinitionsReferenced: ['lab1/artifactextracted/v1'],
    manifestHash,
    exportedByActorId: 'auditor-3',
    exportedAt: '2026-08-11T10:30:00.123-04:00',
    frameworkVersion: '1.0.0',
    rfc3161Timestamp: null,
  }
  return [JSON.stringify(manifest), ...events.map((e) => JSON.stringify(e))].join('\n')
}

describe('OfflineBundleViewer (docs/features/lineage-export-and-playback.md Screen 3)', () => {
  it('reports "Fully independently verified" for a bundle with no masked/erased fields and a valid manifest hash', async () => {
    const ndjson = await makeNdjson(['{"a":1}'])
    const wrapper = mount(OfflineBundleViewer, { props: { bundleNdjson: ndjson } })
    await flushAll()
    expect(wrapper.find('[data-testid="verdict-full"]').exists()).toBe(true)
  })

  it('reports a differentiated partial verdict, never an undifferentiated pass/fail, when a field is masked', async () => {
    const ndjson = await makeNdjson(['{"sourcePath":{"masked":"***"}}'])
    const wrapper = mount(OfflineBundleViewer, { props: { bundleNdjson: ndjson } })
    await flushAll()
    expect(wrapper.find('[data-testid="verdict-partial"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="verdict-full"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="masked-summary"]').text()).toContain('1')
  })

  it('reports a failed verdict when the manifest hash does not match the bundle contents', async () => {
    const ndjson = await makeNdjson(['{"a":1}'])
    const tampered = ndjson.replace(/"manifestHash":"[^"]*"/, '"manifestHash":"tampered"')
    const wrapper = mount(OfflineBundleViewer, { props: { bundleNdjson: tampered } })
    await flushAll()
    expect(wrapper.find('[data-testid="verdict-failed"]').exists()).toBe(true)
  })

  it('renders no masking/claims logic of its own -- a masked leaf renders exactly as it appears in the bundle', async () => {
    const ndjson = await makeNdjson(['{"sourcePath":{"masked":"***"}}'])
    const wrapper = mount(OfflineBundleViewer, { props: { bundleNdjson: ndjson } })
    await flushAll()
    await wrapper.get('[data-testid="toggle-event-list"]').trigger('click')
    expect(wrapper.get('[data-testid="event-list"]').text()).toContain('masked')
  })

  it('surfaces a parse error rather than crashing on malformed input', async () => {
    const wrapper = mount(OfflineBundleViewer, { props: { bundleNdjson: 'not valid ndjson at all {{{' } })
    await flushAll()
    expect(wrapper.find('[data-testid="parse-error"]').exists()).toBe(true)
  })
})
