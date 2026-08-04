import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useEntityViewActions, type ClientConfig } from './useEntityViewActions'
import { resetDbConnectionForTests } from '../db/indexedDb'
import { useOutboxStore } from '../stores/outbox'
import * as publishClientModule from '../api/publishClient'

vi.mock('../api/publishClient', () => ({
  publishCommand: vi.fn(),
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
})
