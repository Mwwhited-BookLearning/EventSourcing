import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useEntityViewActions, type ClientConfig } from './useEntityViewActions'
import { resetDbConnectionForTests } from '../db/indexedDb'
import { useOutboxStore } from '../stores/outbox'
import { useEntityCacheStore } from '../stores/entityCache'
import * as publishClientModule from '../api/publishClient'
import * as graphqlClientModule from '../api/graphqlClient'
import * as streamingClientModule from '../api/streamingClient'

vi.mock('../api/publishClient', () => ({
  publishCommand: vi.fn(),
}))

vi.mock('../api/streamingClient', () => ({
  ingestSamples: vi.fn(),
}))

vi.mock('../api/graphqlClient', () => ({
  graphqlQuery: vi.fn(),
  graphqlSubscribe: vi.fn(),
}))

const config: ClientConfig = {
  instanceId: 'instance-a',
  appId: 'mvvm-demo',
  entityType: 'orderplaced',
  eventType: 'OrderPlaced',
  entityIdField: 'orderId',
  hostBaseUrl: 'https://host.example',
  authBaseUrl: 'https://auth.example',
  clientId: 'follower-client',
  clientSecret: 'secret',
  scope: 'events:follow',
}

describe('useEntityViewActions (docs/patterns/mvvm-client-architecture.md\'s "Actions" layer)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetDbConnectionForTests()
    vi.mocked(publishClientModule.publishCommand).mockReset()
    vi.mocked(streamingClientModule.ingestSamples).mockReset()
    vi.mocked(graphqlClientModule.graphqlQuery).mockReset()
    vi.mocked(graphqlClientModule.graphqlSubscribe).mockReset()
  })

  // ADR-070 -- the seam every IDeviceInputSource adapter's captured
  // reading ultimately reaches, regardless of which adapter (browser API
  // or native bridge) produced it.
  describe('captureDeviceReading (device input integration)', () => {
    it('a discrete-mapped reading is delivered via the ordinary publish path, defaulting to reviewPending', async () => {
      vi.mocked(publishClientModule.publishCommand).mockResolvedValue({ ok: true, status: 'applied', entityId: 'x', conflictFlag: false })
      const fetchToken = vi.fn().mockResolvedValue('token-123')
      const actions = useEntityViewActions(config, { fetchToken })

      await actions.captureDeviceReading(
        { timestamp: '2026-08-11T10:00:00.000Z', value: { bpm: 72 } },
        { kind: 'discrete', appId: 'clinical-1', eventType: 'InstrumentReading', entityId: 'clinical-1:instrumentreading:r-1' },
      )

      expect(publishClientModule.publishCommand).toHaveBeenCalledTimes(1)
      expect(streamingClientModule.ingestSamples).not.toHaveBeenCalled()
      const [, , entry] = vi.mocked(publishClientModule.publishCommand).mock.calls[0]!
      expect(entry.reviewPending).toBe(true)
    })

    it('a continuous-mapped reading is delivered via the streaming ingest path, never as an ordinary publish', async () => {
      vi.mocked(streamingClientModule.ingestSamples).mockResolvedValue({ ok: true, samplesWritten: 1, lateArrivalCount: 0 })
      const fetchToken = vi.fn().mockResolvedValue('token-123')
      const actions = useEntityViewActions(config, { fetchToken })

      await actions.captureDeviceReading(
        { timestamp: '2026-08-11T10:00:00.000Z', value: 98.6, monotonicElapsedMicros: 42_000 },
        { kind: 'continuous', channelId: 'vitals-waveform-1' },
      )

      expect(streamingClientModule.ingestSamples).toHaveBeenCalledTimes(1)
      expect(publishClientModule.publishCommand).not.toHaveBeenCalled()
      const [, , channelId, samples] = vi.mocked(streamingClientModule.ingestSamples).mock.calls[0]!
      expect(channelId).toBe('vitals-waveform-1')
      expect(samples).toEqual([{ timestamp: '2026-08-11T10:00:00.000Z', value: 98.6, monotonicElapsedMicros: 42_000 }])
    })
  })

  it('dispatching a command while online enqueues then delivers immediately, with no duplicate delivery on a redundant flush', async () => {
    vi.mocked(publishClientModule.publishCommand).mockResolvedValue({
      ok: true,
      status: 'applied',
      entityId: 'mvvm-demo:orderplaced:o-1',
      conflictFlag: false,
    })

    const fetchToken = vi.fn().mockResolvedValue('token-123')
    const actions = useEntityViewActions(config, { fetchToken })

    await actions.dispatchCommand('mvvm-demo:orderplaced:o-1', { Amount: 175 })

    expect(publishClientModule.publishCommand).toHaveBeenCalledTimes(1)
    const outbox = useOutboxStore()
    expect(outbox.pendingFor('instance-a')).toHaveLength(0)

    // ADR-011's dedup is what makes this safe -- redelivering the same
    // CommandId after reconnect never applies twice. Here, an already-
    // Delivered entry simply never gets re-sent at all.
    await actions.flush()
    expect(publishClientModule.publishCommand).toHaveBeenCalledTimes(1)
  })

  it('a failed delivery leaves the command queued, redelivered on the next flush', async () => {
    vi.mocked(publishClientModule.publishCommand).mockResolvedValueOnce({ ok: false })
    const fetchToken = vi.fn().mockResolvedValue('token-123')
    const actions = useEntityViewActions(config, { fetchToken })

    await actions.dispatchCommand('mvvm-demo:orderplaced:o-1', { Amount: 175 })
    const outbox = useOutboxStore()
    expect(outbox.pendingFor('instance-a')).toHaveLength(1)

    vi.mocked(publishClientModule.publishCommand).mockResolvedValueOnce({
      ok: true,
      status: 'applied',
      entityId: 'mvvm-demo:orderplaced:o-1',
      conflictFlag: false,
    })
    await actions.flush()
    expect(outbox.pendingFor('instance-a')).toHaveLength(0)
    expect(publishClientModule.publishCommand).toHaveBeenCalledTimes(2)
  })

  it('fetches a token at most once across multiple dispatches', async () => {
    vi.mocked(publishClientModule.publishCommand).mockResolvedValue({ ok: true, status: 'applied', entityId: 'x', conflictFlag: false })
    const fetchToken = vi.fn().mockResolvedValue('token-123')
    const actions = useEntityViewActions(config, { fetchToken })

    await actions.dispatchCommand('mvvm-demo:orderplaced:o-1', { Amount: 175 })
    await actions.dispatchCommand('mvvm-demo:orderplaced:o-1', { Amount: 200 })

    expect(fetchToken).toHaveBeenCalledTimes(1)
  })

  // ADR-065's two rules: an explicit scope filter (the same
  // [EventFilterInput!] shape any GraphQL Subscription already supports),
  // and a mandatory, immediate local purge on EntityErasureRequested --
  // delivered through a second, independent subscription to that reserved
  // type, not folded into the entity subscription's own handler.
  describe('local/edge active-scope caching and erasure invalidation (ADR-065)', () => {
    function mockIntrospection(fieldsByType: { entity: string[]; erasure: string[] }) {
      vi.mocked(graphqlClientModule.graphqlQuery).mockImplementation(async (_host, _token, query) => {
        const fields = (query as string).includes('entityerasurerequested') ? fieldsByType.erasure : fieldsByType.entity
        return { __type: { fields: fields.map((name) => ({ name })) } }
      })
    }

    it('subscribing opens the entity subscription carrying its scope filter, plus a second, independent EntityErasureRequested subscription', async () => {
      mockIntrospection({ entity: ['orderId', 'amount'], erasure: ['targetEntityId'] })
      const subscribedQueries: string[] = []
      vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query) => {
        subscribedQueries.push(query as string)
        return () => {}
      })
      const fetchToken = vi.fn().mockResolvedValue('token-123')
      const scopedConfig: ClientConfig = { ...config, scopeFilter: [{ field: 'Status', eq: 'open' }] }
      const actions = useEntityViewActions(scopedConfig, { fetchToken })

      await actions.subscribe()

      expect(subscribedQueries).toHaveLength(2)
      expect(subscribedQueries[0]).toContain('on_mvvm_demo_orderplaced')
      expect(subscribedQueries[0]).toContain('where: [{field: "Status", eq: "open"}]')
      expect(subscribedQueries[1]).toContain('on_mvvm_demo_entityerasurerequested')
    })

    it('receiving an EntityErasureRequested event for a cached entity purges it immediately, not deferred to any scope-eviction cycle', async () => {
      mockIntrospection({ entity: ['orderId'], erasure: ['targetEntityId'] })
      let erasureOnMessage: ((data: Record<string, { targetEntityId?: string }>) => void) | undefined
      vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query, onMessage) => {
        if ((query as string).includes('entityerasurerequested')) erasureOnMessage = onMessage as typeof erasureOnMessage
        return () => {}
      })
      const fetchToken = vi.fn().mockResolvedValue('token-123')
      const actions = useEntityViewActions(config, { fetchToken })
      const entityCache = useEntityCacheStore()
      await entityCache.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
        conflictFlag: false,
        lateArrivalFlag: false,
        authorityStatus: 'accepted',
        schemaVersion: 1,
        orderId: 'o-1',
      })
      expect(entityCache.get('instance-a', 'mvvm-demo:orderplaced:o-1')).toBeDefined()

      await actions.subscribe()
      erasureOnMessage!({ on_mvvm_demo_entityerasurerequested: { targetEntityId: 'mvvm-demo:orderplaced:o-1' } })

      expect(entityCache.get('instance-a', 'mvvm-demo:orderplaced:o-1')).toBeUndefined()
    })

    it('an EntityErasureRequested event naming a DIFFERENT entity leaves an unrelated cached entity untouched', async () => {
      mockIntrospection({ entity: ['orderId'], erasure: ['targetEntityId'] })
      let erasureOnMessage: ((data: Record<string, { targetEntityId?: string }>) => void) | undefined
      vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query, onMessage) => {
        if ((query as string).includes('entityerasurerequested')) erasureOnMessage = onMessage as typeof erasureOnMessage
        return () => {}
      })
      const fetchToken = vi.fn().mockResolvedValue('token-123')
      const actions = useEntityViewActions(config, { fetchToken })
      const entityCache = useEntityCacheStore()
      await entityCache.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
        conflictFlag: false,
        lateArrivalFlag: false,
        authorityStatus: 'accepted',
        schemaVersion: 1,
        orderId: 'o-1',
      })

      await actions.subscribe()
      erasureOnMessage!({ on_mvvm_demo_entityerasurerequested: { targetEntityId: 'mvvm-demo:orderplaced:o-2' } })

      expect(entityCache.get('instance-a', 'mvvm-demo:orderplaced:o-1')).toBeDefined()
    })
  })
})
