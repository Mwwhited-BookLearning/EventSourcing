import { ref } from 'vue'
import { useOutboxStore } from '../stores/outbox'
import { useEntityCacheStore } from '../stores/entityCache'
import { useViewDefinitionsStore } from '../stores/viewDefinitions'
import type { ClientOutboxEntry, FollowedEventEnvelope } from '../types'
import { fetchToken } from '../api/authClient'
import { publishCommand } from '../api/publishClient'
import { graphqlQuery, graphqlSubscribe } from '../api/graphqlClient'
import { buildIntrospectionQuery, buildSubscriptionQuery, subscriptionFieldName } from '../api/subscriptionBuilder'

// Which EntityType/AppId/event type/subscription target a client instance
// follows is per-instance launch configuration, per ADR-039 -- not a global
// singleton, and not auto-discovered from a registry lookup (that would
// need registry:admin, a scope an ordinary follower credential doesn't
// hold). `entityIdField` is the GraphQL field name (already camelCased,
// e.g. "orderId") the registered EventTypeDefinition.EntityIdField resolves
// to -- whoever configures an instance already knows this, the same way it
// already knows which EntityType/AppId to watch.
export interface ClientConfig {
  instanceId: string
  appId: string
  entityType: string // normalized (lowercase) EntityType, matching the server's own EntityId format
  eventType: string
  entityIdField: string
  hostBaseUrl: string
  authBaseUrl: string
  clientId: string
  clientSecret: string
  scope: string
}

export interface FetchTokenFn {
  (authBaseUrl: string, clientId: string, clientSecret: string, scope: string): Promise<string>
}

// The ViewModel commands layer (docs/patterns/mvvm-client-architecture.md's
// "Actions" role) -- the only layer that enqueues onto ADR-039's client
// outbox, and the only layer that opens the live Subscription that keeps
// the entity cache current. `tokenFetcher`/publish/graphql functions are
// injected (not imported directly) so this composable is testable via
// plain function calls, per that same doc's own "usable without mounting a
// component" rule -- no network, browser, or IndexedDB mocking gymnastics
// needed beyond what the injected functions themselves require.
export function useEntityViewActions(config: ClientConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const outbox = useOutboxStore()
  const entityCache = useEntityCacheStore()
  const viewDefinitions = useViewDefinitionsStore()
  const token = ref<string | null>(null)
  let unsubscribe: (() => void) | null = null

  const tokenFetcher = deps.fetchToken ?? fetchToken

  async function ensureToken(): Promise<string> {
    if (!token.value) token.value = await tokenFetcher(config.authBaseUrl, config.clientId, config.clientSecret, config.scope)
    return token.value
  }

  // Never mutates the entity cache directly (ADR-039) -- enqueues into the
  // durable outbox and, if online, attempts an immediate flush; the
  // confirmed state only lands once the Subscription delivers it back.
  async function dispatchCommand(
    entityId: string,
    patch: Record<string, unknown>,
    schemaVersion = 1,
    expectedVersion: number | null = null,
  ): Promise<void> {
    const entry: ClientOutboxEntry = {
      commandId: crypto.randomUUID(),
      instanceId: config.instanceId,
      appId: config.appId,
      eventType: config.eventType,
      entityId,
      expectedVersion,
      schemaVersion,
      patch: JSON.stringify(patch),
      status: 'Pending',
      enqueuedAt: new Date().toISOString(),
      attempts: 0,
    }
    await outbox.enqueue(entry)
    if (typeof navigator === 'undefined' || navigator.onLine) await flush()
  }

  async function flush(): Promise<void> {
    const currentToken = await ensureToken()
    await outbox.flush((entry) => publishCommand(config.hostBaseUrl, currentToken, entry))
  }

  // Discovers the dynamically-built payload type's own fields via GraphQL
  // introspection (subscriptionBuilder.ts), then opens exactly one
  // Subscription requesting all of them plus the fixed envelope fields
  // (already included, since FollowSubscriptionTypeModule adds them to
  // every payload type unconditionally). Folds each arriving message into
  // the shared entity cache store keyed by this instance + the resolved
  // EntityId.
  async function subscribe(onUpdate?: (entityId: string) => void): Promise<void> {
    const currentToken = await ensureToken()
    const introspection = await graphqlQuery<{ __type: { fields: Array<{ name: string }> } | null }>(
      config.hostBaseUrl,
      currentToken,
      buildIntrospectionQuery(config.appId, config.eventType),
    )
    const fieldNames = introspection.__type?.fields.map((f) => f.name) ?? []
    if (fieldNames.length === 0) return // nothing registered for this event type -- nothing to subscribe to yet

    const query = buildSubscriptionQuery(config.appId, config.eventType, fieldNames)
    const fieldName = subscriptionFieldName(config.appId, config.eventType)

    unsubscribe = graphqlSubscribe<Record<string, FollowedEventEnvelope>>(
      config.hostBaseUrl,
      currentToken,
      query,
      (data) => {
        const payload = data[fieldName]
        if (!payload) return
        const uniqueId = payload[config.entityIdField]
        if (uniqueId === undefined || uniqueId === null) return

        const entityId = `${config.appId}:${config.entityType}:${uniqueId}`
        void entityCache.applyFollowedEvent(config.instanceId, config.entityType, entityId, payload).then(() => onUpdate?.(entityId))
      },
      (error) => {
        // Fail-open, the same posture EventTailReader's own live-read path
        // already uses server-side -- a transient subscription error never
        // crashes the ViewModel; the client keeps rendering its last-known-
        // good cache, offline-safe by construction.
        console.error('Subscription error', error)
      },
    )
  }

  function stopSubscription(): void {
    unsubscribe?.()
    unsubscribe = null
  }

  // EntityView's own lookup step (docs/features/mvvm-client.md's rendering
  // sequence diagram) -- a cache miss (nothing fetched yet, or genuinely
  // nothing registered) is EntityView's signal to render the generic
  // fallback; this composable doesn't distinguish the two cases itself.
  async function loadViewDefinition(viewKind = 'Detail'): Promise<void> {
    const currentToken = await ensureToken()
    await viewDefinitions.fetchAndCache(config.hostBaseUrl, currentToken, config.entityType, viewKind)
  }

  return { dispatchCommand, flush, subscribe, stopSubscription, loadViewDefinition }
}
