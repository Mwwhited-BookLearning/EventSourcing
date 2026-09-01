import { describe, expect, it, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useConnectivityStore } from './connectivity'

describe('ClientConnectivity (manual force-offline/online override)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is effectively online by default, mirroring a real online navigator', () => {
    vi.stubGlobal('navigator', { onLine: true })
    const store = useConnectivityStore()
    expect(store.isEffectivelyOnline()).toBe(true)
    vi.unstubAllGlobals()
  })

  it('goOffline forces isEffectivelyOnline to false even while the real navigator reports online', () => {
    vi.stubGlobal('navigator', { onLine: true })
    const store = useConnectivityStore()
    store.goOffline()
    expect(store.isEffectivelyOnline()).toBe(false)
    vi.unstubAllGlobals()
  })

  it('goOnline clears a prior forced-offline override', () => {
    vi.stubGlobal('navigator', { onLine: true })
    const store = useConnectivityStore()
    store.goOffline()
    store.goOnline()
    expect(store.isEffectivelyOnline()).toBe(true)
    vi.unstubAllGlobals()
  })

  it('never reports effectively online while the real navigator itself is offline, forced override or not', () => {
    vi.stubGlobal('navigator', { onLine: false })
    const store = useConnectivityStore()
    expect(store.isEffectivelyOnline()).toBe(false)
    store.goOnline() // clearing the (already-unset) force doesn't fake real connectivity
    expect(store.isEffectivelyOnline()).toBe(false)
    vi.unstubAllGlobals()
  })

  it('re-reads real navigator connectivity fresh on every call, never caching a stale value (the reason this is a plain method, not a Pinia getter/computed)', () => {
    vi.stubGlobal('navigator', { onLine: true })
    const store = useConnectivityStore()
    expect(store.isEffectivelyOnline()).toBe(true)

    // The real browser signal flips with NO change to forcedOffline --
    // a cached `computed` getter would still return the stale `true` here.
    vi.stubGlobal('navigator', { onLine: false })
    expect(store.isEffectivelyOnline()).toBe(false)

    vi.stubGlobal('navigator', { onLine: true })
    expect(store.isEffectivelyOnline()).toBe(true)
    vi.unstubAllGlobals()
  })
})
