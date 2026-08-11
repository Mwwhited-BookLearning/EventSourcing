import { describe, expect, it, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useEntityCacheStore } from './entityCache'
import { resetDbConnectionForTests } from '../db/indexedDb'

describe('ClientEntityCache (ADR-039)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetDbConnectionForTests()
  })

  it('folds an arriving Subscription payload, including the shared envelope flags', async () => {
    const store = useEntityCacheStore()
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      amount: 150,
      conflictFlag: true,
      lateArrivalFlag: false,
      authorityStatus: 'pending_review',
      schemaVersion: 1,
    })

    const entry = store.get('instance-a', 'mvvm-demo:orderplaced:o-1')
    expect(entry).toBeDefined()
    expect(entry!.data.orderId).toBe('o-1')
    expect(entry!.data.amount).toBe(150)
    // ADR-024/029/035, "one shared generic flag convention" -- all three
    // travel through unchanged, never collapsed into a single status.
    expect(entry!.conflictFlag).toBe(true)
    expect(entry!.lateArrivalFlag).toBe(false)
    expect(entry!.authorityStatus).toBe('pending_review')
  })

  it('a later arriving event updates the cache without discarding fields the new event does not mention', async () => {
    const store = useEntityCacheStore()
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      amount: 150,
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      carrier: 'UPS',
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })

    const entry = store.get('instance-a', 'mvvm-demo:orderplaced:o-1')
    expect(entry!.data.amount).toBe(150)
    expect(entry!.data.carrier).toBe('UPS')
  })

  it('the cache survives a client process restart via IndexedDB', async () => {
    const store = useEntityCacheStore()
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      amount: 150,
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })

    setActivePinia(createPinia())
    const restarted = useEntityCacheStore()
    await restarted.loadFromDb('instance-a')
    expect(restarted.get('instance-a', 'mvvm-demo:orderplaced:o-1')?.data.amount).toBe(150)
  })

  it('purging an entity removes it from both memory and IndexedDB (ADR-065)', async () => {
    const store = useEntityCacheStore()
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })

    await store.purge('instance-a', 'mvvm-demo:orderplaced:o-1')
    expect(store.get('instance-a', 'mvvm-demo:orderplaced:o-1')).toBeUndefined()

    setActivePinia(createPinia())
    const restarted = useEntityCacheStore()
    await restarted.loadFromDb('instance-a')
    expect(restarted.get('instance-a', 'mvvm-demo:orderplaced:o-1')).toBeUndefined()
  })
})
