import { describe, expect, it, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useOutboxStore } from './outbox'
import { resetDbConnectionForTests } from '../db/indexedDb'
import type { ClientOutboxEntry } from '../types'

function makeEntry(overrides: Partial<ClientOutboxEntry> = {}): ClientOutboxEntry {
  return {
    commandId: 'cmd-1',
    instanceId: 'instance-a',
    appId: 'mvvm-demo',
    eventType: 'OrderPlaced',
    entityId: 'mvvm-demo:orderplaced:o-1',
    expectedVersion: null,
    schemaVersion: 1,
    patch: JSON.stringify({ Amount: 175.0 }),
    status: 'Pending',
    enqueuedAt: new Date().toISOString(),
    attempts: 0,
    ...overrides,
  }
}

describe('ClientOutbox (ADR-039)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetDbConnectionForTests()
  })

  it('a command dispatched while offline queues durably and is never lost', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry())

    expect(store.pendingFor('instance-a')).toHaveLength(1)

    // Simulates a client process restart: a FRESH store instance loads
    // purely from IndexedDB, never from the in-memory state above.
    setActivePinia(createPinia())
    const restarted = useOutboxStore()
    await restarted.loadFromDb('instance-a')
    expect(restarted.pendingFor('instance-a')).toHaveLength(1)
    expect(restarted.pendingFor('instance-a')[0]?.commandId).toBe('cmd-1')
  })

  it('a queued command applies once connectivity resumes, with no duplicate application', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry())

    let publishCallCount = 0
    const publish = async () => {
      publishCallCount += 1
      return { ok: true }
    }

    await store.flush(publish)
    expect(publishCallCount).toBe(1)
    expect(store.pendingFor('instance-a')).toHaveLength(0)

    // Redelivering (e.g. a second, redundant flush trigger, ADR-069) must
    // not re-publish an already-Delivered entry.
    await store.flush(publish)
    expect(publishCallCount).toBe(1)
  })

  it('routes a streamingSample entry to ingestSample, never to publish (ADR-070 per-integration schema choice)', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry({ commandId: 'cmd-continuous', deliveryKind: 'streamingSample', channelId: 'vitals-waveform-1', appId: '', eventType: '', entityId: '' }))

    let publishCalls = 0
    let ingestCalls = 0
    await store.flush(
      async () => { publishCalls += 1; return { ok: true } },
      async () => { ingestCalls += 1; return { ok: true } },
    )

    expect(publishCalls).toBe(0)
    expect(ingestCalls).toBe(1)
    expect(store.pendingFor('instance-a')).toHaveLength(0)
  })

  it('leaves a streamingSample entry Pending, never misrouted to publish, when no ingestSample function is supplied', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry({ commandId: 'cmd-continuous-2', deliveryKind: 'streamingSample', channelId: 'vitals-waveform-1', appId: '', eventType: '', entityId: '' }))

    let publishCalls = 0
    await store.flush(async () => { publishCalls += 1; return { ok: true } })

    expect(publishCalls).toBe(0)
    expect(store.pendingFor('instance-a')).toHaveLength(1)
  })

  it('a failed delivery leaves the entry Pending, retried on the next flush', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry())

    await store.flush(async () => ({ ok: false }))
    expect(store.pendingFor('instance-a')).toHaveLength(1)
    expect(store.pendingFor('instance-a')[0]?.attempts).toBe(1)

    await store.flush(async () => ({ ok: true }))
    expect(store.pendingFor('instance-a')).toHaveLength(0)
  })

  it('exports pending entries to a verifiable bundle and imports them into a fresh instance (ADR-069 sneakernet transfer)', async () => {
    const source = useOutboxStore()
    await source.enqueue(makeEntry({ commandId: 'cmd-export-1' }))
    const bundle = await source.exportBundle('instance-a')

    setActivePinia(createPinia())
    const destination = useOutboxStore()
    const result = await destination.importBundle(bundle)

    expect(result.verified).toBe(true)
    expect(result.importedCount).toBe(1)
    expect(destination.pendingFor('instance-a')).toHaveLength(1)
    expect(destination.pendingFor('instance-a')[0]?.commandId).toBe('cmd-export-1')
  })

  it('importing the same bundle twice never duplicates an already-present command', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry({ commandId: 'cmd-export-2' }))
    const bundle = await store.exportBundle('instance-a')

    const first = await store.importBundle(bundle)
    const second = await store.importBundle(bundle)

    expect(first.importedCount).toBe(0) // already present locally -- exported from THIS same store
    expect(second.importedCount).toBe(0)
    expect(store.pendingFor('instance-a')).toHaveLength(1)
  })

  it('two client instances scoped to different entity types never share outbox state', async () => {
    const store = useOutboxStore()
    await store.enqueue(makeEntry({ commandId: 'cmd-a', instanceId: 'instance-a' }))
    await store.enqueue(makeEntry({ commandId: 'cmd-b', instanceId: 'instance-b', entityId: 'mvvm-demo:shipment:s-1' }))

    expect(store.pendingFor('instance-a')).toHaveLength(1)
    expect(store.pendingFor('instance-b')).toHaveLength(1)

    await store.flush(async () => ({ ok: false })) // fails everything currently in memory -- both entries loaded via enqueue above
    // instance-a's own entry is unaffected by instance-b's delivery outcome and vice versa -- each entry tracks its own attempts independently.
    expect(store.pendingFor('instance-a')[0]?.attempts).toBe(1)
    expect(store.pendingFor('instance-b')[0]?.attempts).toBe(1)
  })
})
