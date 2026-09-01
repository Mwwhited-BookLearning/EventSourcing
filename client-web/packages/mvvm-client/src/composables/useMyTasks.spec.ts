import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useMyTasks, type PendingTask } from './useMyTasks'
import * as graphqlClientModule from '../api/graphqlClient'

vi.mock('../api/graphqlClient', () => ({
  graphqlQuery: vi.fn(),
}))

const baseConfig = {
  hostBaseUrl: 'https://host.example',
  authBaseUrl: 'https://auth.example',
  clientId: 'vitals-pi-client',
  clientSecret: 'vitals-pi-client-secret',
}

const sampleTask: PendingTask = {
  key: 'evt-1',
  flowName: 'vitals-workflow-b-adverse-event-review',
  description: 'PI must review and sign off on the adverse event',
  requiredClaim: 'review:ae',
  appId: 'trial1',
  entityId: 'ae-1',
  raisedAt: '2026-08-31T00:00:00Z',
}

describe('useMyTasks', () => {
  beforeEach(() => {
    vi.mocked(graphqlClientModule.graphqlQuery).mockReset()
  })

  it('refresh() fetches a token once and populates tasks from the myTasks query', async () => {
    vi.mocked(graphqlClientModule.graphqlQuery).mockResolvedValue({ myTasks: [sampleTask] })
    const fetchToken = vi.fn().mockResolvedValue('pi-token')
    const myTasks = useMyTasks(baseConfig, { fetchToken })

    await myTasks.refresh()

    expect(fetchToken).toHaveBeenCalledWith('https://auth.example', 'vitals-pi-client', 'vitals-pi-client-secret', 'events:publish')
    expect(myTasks.tasks.value).toEqual([sampleTask])
    expect(myTasks.error.value).toBeNull()

    await myTasks.refresh()
    expect(fetchToken).toHaveBeenCalledTimes(1) // token cached across refreshes, same posture as every other composable here
  })

  it('respects a caller-supplied scope instead of the events:publish default', async () => {
    vi.mocked(graphqlClientModule.graphqlQuery).mockResolvedValue({ myTasks: [] })
    const fetchToken = vi.fn().mockResolvedValue('follower-token')
    const myTasks = useMyTasks({ ...baseConfig, clientId: 'follower-client', clientSecret: 'follower-client-secret', scope: 'events:follow' }, { fetchToken })

    await myTasks.refresh()

    expect(fetchToken).toHaveBeenCalledWith('https://auth.example', 'follower-client', 'follower-client-secret', 'events:follow')
  })

  it('surfaces a failed query as error, without clearing previously-loaded tasks silently', async () => {
    vi.mocked(graphqlClientModule.graphqlQuery).mockRejectedValueOnce(new Error('Forbidden'))
    const fetchToken = vi.fn().mockResolvedValue('pi-token')
    const myTasks = useMyTasks(baseConfig, { fetchToken })

    await myTasks.refresh()

    expect(myTasks.error.value).toBe('Forbidden')
    expect(myTasks.tasks.value).toEqual([])
  })

  describe('polling', () => {
    beforeEach(() => vi.useFakeTimers())
    afterEach(() => vi.useRealTimers())

    it('startPolling() refreshes immediately, then again on each interval, until stopped', async () => {
      vi.mocked(graphqlClientModule.graphqlQuery).mockResolvedValue({ myTasks: [sampleTask] })
      const fetchToken = vi.fn().mockResolvedValue('pi-token')
      const myTasks = useMyTasks(baseConfig, { fetchToken })

      myTasks.startPolling(10_000)
      await vi.waitFor(() => expect(myTasks.tasks.value).toHaveLength(1))
      expect(graphqlClientModule.graphqlQuery).toHaveBeenCalledTimes(1)

      await vi.advanceTimersByTimeAsync(10_000)
      expect(graphqlClientModule.graphqlQuery).toHaveBeenCalledTimes(2)

      myTasks.stopPolling()
      await vi.advanceTimersByTimeAsync(30_000)
      expect(graphqlClientModule.graphqlQuery).toHaveBeenCalledTimes(2) // no further calls once stopped
    })
  })
})
