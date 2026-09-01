import { ref } from 'vue'
import { fetchToken } from '../api/authClient'
import { graphqlQuery } from '../api/graphqlClient'
import type { FetchTokenFn } from './useEntityViewActions'

// ADR-101 -- the flow engine's own PendingTask read model, exposed as the
// single cross-domain "myTasks" GraphQL query (EventStore.GraphQL.
// PendingTaskQueries). Deliberately a plain query, not a subscription --
// there is no myTasks Subscription field to consume (the caller's own
// "just a query... fed from events like everything else" instruction, see
// docs/comparisons/user-flow-dsl.md), so this composable polls rather than
// mirroring usePendingAuthorityQueue's own live-subscription shape.
export interface PendingTask {
  key: string
  flowName: string
  description: string
  requiredClaim: string | null
  appId: string
  entityId: string
  raisedAt: string
}

export interface MyTasksConfig {
  hostBaseUrl: string
  authBaseUrl: string
  // A real reviewer identity (vitals-pi-client/meridian-analyst-client),
  // never composer-client/follower-client's generic identities -- RequiredClaim
  // filtering (PendingTaskQueries.GetMyTasksAsync) only ever returns a claimed
  // task to a caller whose own token actually carries that claim.
  clientId: string
  clientSecret: string
  scope?: string
}

const MY_TASKS_QUERY = `{ myTasks { key flowName description requiredClaim appId entityId raisedAt } }`

export function useMyTasks(config: MyTasksConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken

  const tasks = ref<PendingTask[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  let token: string | null = null
  let pollHandle: ReturnType<typeof setInterval> | null = null

  async function ensureToken(): Promise<string> {
    token ??= await tokenFetcher(config.authBaseUrl, config.clientId, config.clientSecret, config.scope ?? 'events:publish')
    return token
  }

  async function refresh(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const currentToken = await ensureToken()
      const result = await graphqlQuery<{ myTasks: PendingTask[] }>(config.hostBaseUrl, currentToken, MY_TASKS_QUERY)
      tasks.value = result.myTasks
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      loading.value = false
    }
  }

  // 10s -- close enough to "live" for a task list without building a second
  // subscription-shaped mechanism for a read model this repo's own design
  // explicitly wanted kept to "just a query" (see this file's own header).
  function startPolling(intervalMs = 10_000): void {
    stopPolling()
    void refresh()
    pollHandle = setInterval(() => void refresh(), intervalMs)
  }

  function stopPolling(): void {
    if (pollHandle !== null) {
      clearInterval(pollHandle)
      pollHandle = null
    }
  }

  return { tasks, loading, error, refresh, startPolling, stopPolling }
}
