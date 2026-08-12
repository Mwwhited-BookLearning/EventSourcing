import { ref } from 'vue'
import { useOutboxStore } from '../stores/outbox'
import { useEntityCacheStore } from '../stores/entityCache'
import { useViewDefinitionsStore } from '../stores/viewDefinitions'
import type { ClientOutboxEntry, FollowedEventEnvelope } from '../types'
import { fetchToken } from '../api/authClient'
import { publishCommand } from '../api/publishClient'
import { ingestSamples } from '../api/streamingClient'
import { graphqlQuery, graphqlSubscribe } from '../api/graphqlClient'
import {
  buildIntrospectionQuery,
  buildSubscriptionQuery,
  subscriptionFieldName,
  toSubscriptionFieldSelectors,
  type IntrospectedField,
  type ScopeFilterClause,
} from '../api/subscriptionBuilder'
import type { DeviceReading } from '../deviceInput/types'
import { toOutboxEntry, type ReadingMapping } from '../deviceInput/deviceReadingOutbox'
import { negotiateLocale } from '../api/localeClient'
import { resolveTranslations } from '../i18n/translations'

// ADR-057's reserved, lazily-registered event type -- EntityStore.Erasure/
// EntityErasureRequestedEventType.cs's own Name, server-side. Only ever
// exists (is introspectable) for an AppId once that AppId's first classified
// field has been published; buildIntrospectionQuery/subscribe's own existing
// "fieldNames.length === 0 -> nothing to subscribe to yet" guard already
// covers the case where it doesn't exist yet for this instance's AppId.
const ERASURE_EVENT_TYPE = 'EntityErasureRequested'

interface ErasureEventPayload {
  targetEntityId?: string
}

// Which EntityType/AppId/event type/subscription target a client instance
// follows is per-instance launch configuration, per ADR-039 -- not a global
// singleton, and not auto-discovered from a registry lookup (that would
// need registry:admin, a scope an ordinary follower credential doesn't
// hold). `entityIdField` is the GraphQL field name (already camelCased,
// e.g. "orderId") the registered EventTypeDefinition.EntityIdField resolves
// to -- whoever configures an instance already knows this, the same way it
// already knows which EntityType/AppId to watch. `scopeFilter` (ADR-065) is
// the same [EventFilterInput!] shape any GraphQL Subscription already
// supports (ADR-037) -- optional, since not every instance needs to narrow
// its cache to an active subset; omitting it subscribes unfiltered, exactly
// as every instance did before this item.
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
  scopeFilter?: ScopeFilterClause[]
  // ADR-087 -- the Accept-Language VALUE this instance sends; defaults to
  // the browser's own negotiated preference, never a bespoke locale query
  // parameter. The SERVER'S negotiated response (Content-Language, which
  // may legitimately differ -- e.g. an unsupported culture falls back to
  // the server's own default) is what actually drives translation-key
  // resolution, not this raw request value.
  acceptLanguage?: string
}

export interface FetchTokenFn {
  // `acr` is ADR-066/RFC 9470's dev-simulated step-up parameter (authClient.ts)
  // -- optional so every existing caller/mock with the original 4-arg shape
  // is unaffected; only useEventComposer's step-up retry path passes it.
  (authBaseUrl: string, clientId: string, clientSecret: string, scope: string, acr?: string): Promise<string>
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
  const locale = ref('en-US')
  const translations = ref<Record<string, string>>(resolveTranslations('en-US'))
  let unsubscribeEntity: (() => void) | null = null
  let unsubscribeErasure: (() => void) | null = null

  const tokenFetcher = deps.fetchToken ?? fetchToken

  // ADR-087 -- negotiates once per instance (locale doesn't change mid-
  // session in this client), reading the server's own resolved
  // Content-Language rather than trusting the browser's preference
  // directly. EntityView calls this alongside loadViewDefinition so a
  // TemplateRenderer always has a real, server-confirmed locale by the
  // time it first renders.
  async function resolveLocale(): Promise<void> {
    const acceptLanguage = config.acceptLanguage ?? (typeof navigator === 'undefined' ? 'en-US' : navigator.language)
    locale.value = await negotiateLocale(config.hostBaseUrl, acceptLanguage)
    translations.value = resolveTranslations(locale.value)
  }

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
    await outbox.flush(
      (entry) => publishCommand(config.hostBaseUrl, currentToken, entry),
      (entry) => ingestSamples(config.hostBaseUrl, currentToken, entry.channelId!, [
        { timestamp: entry.enqueuedAt, value: JSON.parse(entry.patch), monotonicElapsedMicros: entry.monotonicElapsedMicros },
      ]),
    )
  }

  // ADR-070 -- the ONE thing every IDeviceInputSource adapter's captured
  // reading feeds into, regardless of which browser API (or the native
  // bridge) produced it: the same durable outbox any other client-
  // originated write already uses, immediately attempting a flush if
  // online (identical posture to dispatchCommand above). Which mapping
  // applies (discrete publish vs. continuous streaming sample) is the
  // caller's own per-integration configuration, never inferred here.
  async function captureDeviceReading(reading: DeviceReading, mapping: ReadingMapping): Promise<void> {
    await outbox.enqueue(toOutboxEntry(config.instanceId, reading, mapping))
    if (typeof navigator === 'undefined' || navigator.onLine) await flush()
  }

  // Discovers the dynamically-built payload type's own fields via GraphQL
  // introspection (subscriptionBuilder.ts), then opens exactly one
  // Subscription requesting all of them plus the fixed envelope fields
  // (already included, since FollowSubscriptionTypeModule adds them to
  // every payload type unconditionally). Folds each arriving message into
  // the shared entity cache store keyed by this instance + the resolved
  // EntityId. `config.scopeFilter` (ADR-065), when supplied, is forwarded
  // as the Subscription's own `where` argument -- narrowing what the
  // server ever delivers (and therefore what this cache ever holds) to the
  // active-scoped subset, the entire mechanism this ADR calls for. Honest,
  // named limitation, not silently glossed over: because the filter is
  // enforced server-side per event, an entity that later stops matching
  // (closed, completed, reassigned) simply stops receiving further updates
  // through this connection -- there is no push-based "you fell out of
  // scope, evict now" signal, so an already-cached copy is not proactively
  // purged the moment that happens. It goes stale rather than being
  // actively wrong (no further writes reach it), and a fresh reconnect
  // with the same filter never re-delivers it. Erasure (below) has no such
  // gap, since ADR-057 makes it an ordinary delivered event, not a filter
  // outcome.
  async function subscribeToEntity(onUpdate?: (entityId: string) => void): Promise<void> {
    const currentToken = await ensureToken()
    const introspection = await graphqlQuery<{ __type: { fields: IntrospectedField[] } | null }>(
      config.hostBaseUrl,
      currentToken,
      buildIntrospectionQuery(config.appId, config.eventType),
    )
    const fields = introspection.__type?.fields ?? []
    if (fields.length === 0) return // nothing registered for this event type -- nothing to subscribe to yet

    // REPLAY from 0, not TAIL -- EventTailReader's single poll loop keeps
    // running past its starting cursor regardless of mode, so this
    // delivers already-published history AND every subsequent live event
    // through the one subscription (build-plan item "Proving-Ground
    // Application UX"). No persisted per-instance cursor yet (TODO.md's
    // fuller resume-cursor mechanism) -- every fresh subscribe still
    // replays from the very start, which is the right trade-off for a
    // demo/proving-ground instance and an honest, small scope narrowing
    // for a long-lived production deployment with a large history.
    const query = buildSubscriptionQuery(config.appId, config.eventType, toSubscriptionFieldSelectors(fields), config.scopeFilter, 'REPLAY', 0)
    const fieldName = subscriptionFieldName(config.appId, config.eventType)

    unsubscribeEntity = graphqlSubscribe<Record<string, FollowedEventEnvelope>>(
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

  // ADR-065's mandatory, immediate local purge on erasure -- a SECOND,
  // generic subscription (the same introspect-then-subscribe shape as
  // subscribeToEntity above) to the reserved EntityErasureRequested type
  // for this instance's own AppId, independent of whatever entity type
  // it's otherwise watching. `EntityErasureRequested` reaches a subscribed
  // client through the exact same channel as any other update (ADR-057) --
  // there is nothing special-cased about the transport, only about what
  // this handler does with it: delete the named entity's cached copy right
  // away, never deferred to the next scope-eviction cycle. Named
  // limitation shared with the ADR itself: a device offline at the moment
  // erasure fires won't purge until it reconnects and receives this event.
  async function subscribeToErasure(): Promise<void> {
    const currentToken = await ensureToken()
    const introspection = await graphqlQuery<{ __type: { fields: IntrospectedField[] } | null }>(
      config.hostBaseUrl,
      currentToken,
      buildIntrospectionQuery(config.appId, ERASURE_EVENT_TYPE),
    )
    // EntityErasureRequested's own schema (TargetEntityId only) never
    // carries a masked field -- bare names, same as before this fix.
    const fieldNames = introspection.__type?.fields.map((f) => f.name) ?? []
    if (fieldNames.length === 0) return // this AppId has never published a classified field -- nothing to erase yet

    const query = buildSubscriptionQuery(config.appId, ERASURE_EVENT_TYPE, fieldNames)
    const fieldName = subscriptionFieldName(config.appId, ERASURE_EVENT_TYPE)

    unsubscribeErasure = graphqlSubscribe<Record<string, ErasureEventPayload>>(
      config.hostBaseUrl,
      currentToken,
      query,
      (data) => {
        const targetEntityId = data[fieldName]?.targetEntityId
        if (targetEntityId) void entityCache.purge(config.instanceId, targetEntityId)
      },
      (error) => console.error('Erasure subscription error', error),
    )
  }

  async function subscribe(onUpdate?: (entityId: string) => void): Promise<void> {
    await subscribeToEntity(onUpdate)
    await subscribeToErasure()
  }

  function stopSubscription(): void {
    unsubscribeEntity?.()
    unsubscribeEntity = null
    unsubscribeErasure?.()
    unsubscribeErasure = null
  }

  // EntityView's own lookup step (docs/features/mvvm-client.md's rendering
  // sequence diagram) -- a cache miss (nothing fetched yet, or genuinely
  // nothing registered) is EntityView's signal to render the generic
  // fallback; this composable doesn't distinguish the two cases itself.
  async function loadViewDefinition(viewKind = 'Detail'): Promise<void> {
    const currentToken = await ensureToken()
    await viewDefinitions.fetchAndCache(config.hostBaseUrl, currentToken, config.entityType, viewKind)
  }

  return { dispatchCommand, flush, subscribe, stopSubscription, loadViewDefinition, captureDeviceReading, resolveLocale, locale, translations }
}
