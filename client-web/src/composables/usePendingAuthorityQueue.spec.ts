import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePendingAuthorityQueue } from './usePendingAuthorityQueue'
import * as graphqlClientModule from '../api/graphqlClient'
import * as publishClientModule from '../api/publishClient'

vi.mock('../api/graphqlClient', () => ({
  graphqlQuery: vi.fn(),
  graphqlSubscribe: vi.fn(),
}))

vi.mock('../api/publishClient', () => ({
  publishCommand: vi.fn(),
}))

const baseConfig = {
  hostBaseUrl: 'https://host.example',
  authBaseUrl: 'https://auth.example',
  appId: 'trial1',
  raiserEventType: 'IonmAlertRaised',
  decisionClientId: 'vitals-pi-client',
  decisionClientSecret: 'vitals-pi-client-secret',
}

function mockIntrospection(fieldsByType: { raiser: string[]; decision: string[] }): void {
  vi.mocked(graphqlClientModule.graphqlQuery).mockImplementation(async (_host, _token, query) => {
    const fields = (query as string).includes('authoritydecision') ? fieldsByType.decision : fieldsByType.raiser
    return { __type: { fields: fields.map((name) => ({ name })) } }
  })
}

describe('usePendingAuthorityQueue', () => {
  beforeEach(() => {
    vi.mocked(graphqlClientModule.graphqlQuery).mockReset()
    vi.mocked(graphqlClientModule.graphqlSubscribe).mockReset()
    vi.mocked(publishClientModule.publishCommand).mockReset()
  })

  it('subscribes to both the raiser event type and the shared authorityDecision type', async () => {
    mockIntrospection({ raiser: ['eventId', 'alertId', 'authorityStatus'], decision: ['targetEventId', 'decision'] })
    const fetchToken = vi.fn().mockResolvedValue('follower-token')
    const subscribedQueries: string[] = []
    vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query) => {
      subscribedQueries.push(query as string)
      return () => {}
    })

    const queue = usePendingAuthorityQueue({ ...baseConfig, isPending: (p) => p.authorityStatus === 'pending_review' }, { fetchToken })
    await queue.subscribe()

    expect(subscribedQueries).toHaveLength(2)
    expect(subscribedQueries.some((q) => q.includes('on_trial1_ionmalertraised'))).toBe(true)
    expect(subscribedQueries.some((q) => q.includes('on_trial1_authoritydecision'))).toBe(true)
  })

  it('adds a raiser event to the queue only when isPending returns true', async () => {
    mockIntrospection({ raiser: ['eventId', 'alertId', 'authorityStatus'], decision: ['targetEventId'] })
    let raiserOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query, onMessage) => {
      if ((query as string).includes('ionmalertraised')) raiserOnMessage = onMessage as typeof raiserOnMessage
      return () => {}
    })
    const fetchToken = vi.fn().mockResolvedValue('follower-token')
    const queue = usePendingAuthorityQueue({ ...baseConfig, isPending: (p) => p.authorityStatus === 'pending_review' }, { fetchToken })
    await queue.subscribe()

    raiserOnMessage!({ on_trial1_ionmalertraised: { eventId: 'evt-1', alertId: 'alert-1', authorityStatus: 'accepted' } })
    expect(queue.items.value).toHaveLength(0)

    raiserOnMessage!({ on_trial1_ionmalertraised: { eventId: 'evt-2', alertId: 'alert-2', authorityStatus: 'pending_review' } })
    expect(queue.items.value).toHaveLength(1)
    expect(queue.items.value[0]).toEqual({ eventId: 'evt-2', payload: { eventId: 'evt-2', alertId: 'alert-2', authorityStatus: 'pending_review' } })
  })

  it('removes a queued item once a matching authorityDecision arrives', async () => {
    mockIntrospection({ raiser: ['eventId', 'alertId', 'authorityStatus'], decision: ['targetEventId'] })
    let raiserOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    let decisionOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    vi.mocked(graphqlClientModule.graphqlSubscribe).mockImplementation((_host, _token, query, onMessage) => {
      if ((query as string).includes('ionmalertraised')) raiserOnMessage = onMessage as typeof raiserOnMessage
      if ((query as string).includes('authoritydecision')) decisionOnMessage = onMessage as typeof decisionOnMessage
      return () => {}
    })
    const fetchToken = vi.fn().mockResolvedValue('follower-token')
    const queue = usePendingAuthorityQueue({ ...baseConfig, isPending: (p) => p.authorityStatus === 'pending_review' }, { fetchToken })
    await queue.subscribe()

    raiserOnMessage!({ on_trial1_ionmalertraised: { eventId: 'evt-2', alertId: 'alert-2', authorityStatus: 'pending_review' } })
    expect(queue.items.value).toHaveLength(1)

    decisionOnMessage!({ on_trial1_authoritydecision: { targetEventId: 'evt-2' } })
    expect(queue.items.value).toHaveLength(0)
  })

  it('decide() publishes authorityDecision via the decision identity, with Meaning', async () => {
    vi.mocked(publishClientModule.publishCommand).mockResolvedValue({ ok: true, status: 'received', entityId: 'trial1:authoritydecision:evt-2', conflictFlag: false })
    const fetchToken = vi.fn().mockResolvedValue('vitals-pi-token')
    const queue = usePendingAuthorityQueue({ ...baseConfig, isPending: () => true }, { fetchToken })

    const result = await queue.decide('evt-2', 'accepted', 'pi-1', 'confirmed real finding', 'reviewed')

    expect(result.ok).toBe(true)
    expect(fetchToken).toHaveBeenCalledWith('https://auth.example', 'vitals-pi-client', 'vitals-pi-client-secret', 'events:publish')
    const [hostBaseUrl, token, entry] = vi.mocked(publishClientModule.publishCommand).mock.calls[0]
    expect(hostBaseUrl).toBe('https://host.example')
    expect(token).toBe('vitals-pi-token')
    expect(entry.eventType).toBe('authorityDecision')
    expect(JSON.parse(entry.patch)).toEqual({ targetEventId: 'evt-2', decision: 'accepted', decidingActorId: 'pi-1', reason: 'confirmed real finding' })
    expect(entry.meaning).toBe('reviewed')
  })
})
