import { ref } from 'vue'
import { fetchToken } from '../api/authClient'
import { graphqlQuery, graphqlSubscribe } from '../api/graphqlClient'
import { buildIntrospectionQuery, buildSubscriptionQuery, subscriptionFieldName, toSubscriptionFieldSelectors, type IntrospectedField } from '../api/subscriptionBuilder'
import { useEventComposer } from './useEventComposer'
import type { FetchTokenFn } from './useEntityViewActions'

// "Domain Decision Queues" -- both proving-ground domains' shared
// "authorityDecision" reactor (VitalsSharedTypes/MeridianSharedTypes'
// EnsureAuthorityDecisionRegisteredAsync), never a per-domain type name.
const AUTHORITY_DECISION_TYPE = 'authorityDecision'

export interface PendingAuthorityQueueConfig {
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
  // The event type a pending item is RAISED as (e.g. "IonmAlertRaised",
  // "SanctionsScreeningPerformed") -- deliberately generic: this composable
  // never hardcodes a Vitals or Meridian field name, the same discipline
  // useEventComposer's own Decision text already establishes.
  raiserEventType: string
  // "Does this raiser event still need a decision" -- inherently domain
  // business logic (AuthorityStatus for Vitals' non-authoritative IONM
  // alerts; a plain MatchFound field for Meridian's ordinary, always-
  // "accepted" sanctions hits), so the CALLER supplies it rather than this
  // composable guessing at one universal rule.
  isPending: (payload: Record<string, unknown>) => boolean
  // A real, distinct identity per real-world reviewer role (vitals-pi-client/
  // meridian-analyst-client) -- never composer-client's own generic identity,
  // see DevIdpSeeder.cs's own comment on why. events:publish only.
  decisionClientId: string
  decisionClientSecret: string
  followerClientId?: string
  followerClientSecret?: string
}

export interface PendingAuthorityItem {
  eventId: string
  payload: Record<string, unknown>
}

export function usePendingAuthorityQueue(config: PendingAuthorityQueueConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken
  const followerClientId = config.followerClientId ?? 'follower-client'
  const followerClientSecret = config.followerClientSecret ?? 'follower-client-secret'

  const items = ref<PendingAuthorityItem[]>([])
  let unsubscribeRaiser: (() => void) | null = null
  let unsubscribeDecision: (() => void) | null = null
  let followerToken: string | null = null

  // A SEPARATE, events:publish-only identity from the read-side follower
  // token above -- the same split useEventComposer already establishes
  // between listing/subscribing (registry:admin/events:follow) and
  // publishing (events:publish), applied here across two DIFFERENT
  // people's real-world roles rather than one tool's two scopes.
  const decisionComposer = useEventComposer(
    {
      hostBaseUrl: config.hostBaseUrl,
      authBaseUrl: config.authBaseUrl,
      appId: config.appId,
      composerClientId: config.decisionClientId,
      composerClientSecret: config.decisionClientSecret,
      scope: 'events:publish',
    },
    deps,
  )

  async function ensureFollowerToken(): Promise<string> {
    followerToken ??= await tokenFetcher(config.authBaseUrl, followerClientId, followerClientSecret, 'events:follow')
    return followerToken
  }

  // Mirrors useEntityViewActions.subscribeToErasure's own introspect-then-
  // subscribe shape exactly -- REPLAY from 0 so a freshly-opened queue shows
  // already-pending items immediately, not just ones raised after the tab
  // connected (the same "Proving-Ground Application UX" fix already applied
  // to the generic Detail/Browse tabs).
  async function subscribeToRaiser(): Promise<void> {
    const token = await ensureFollowerToken()
    const introspection = await graphqlQuery<{ __type: { fields: IntrospectedField[] } | null }>(
      config.hostBaseUrl,
      token,
      buildIntrospectionQuery(config.appId, config.raiserEventType),
    )
    const fields = introspection.__type?.fields ?? []
    if (fields.length === 0) return // nothing registered for this event type yet

    const query = buildSubscriptionQuery(config.appId, config.raiserEventType, toSubscriptionFieldSelectors(fields), undefined, 'REPLAY', 0)
    const fieldName = subscriptionFieldName(config.appId, config.raiserEventType)

    unsubscribeRaiser = graphqlSubscribe<Record<string, Record<string, unknown>>>(
      config.hostBaseUrl,
      token,
      query,
      (data) => {
        const payload = data[fieldName]
        if (!payload) return
        const eventId = payload.eventId as string | undefined
        if (!eventId || !config.isPending(payload)) return
        if (items.value.some((item) => item.eventId === eventId)) return
        items.value = [...items.value, { eventId, payload }]
      },
      (error) => console.error('Pending-queue raiser subscription error', error),
    )
  }

  // A decision resolves the queue item it targets the moment it arrives,
  // through the same live channel as any other event -- AuthorityDecisionResolver
  // mutates the TARGET event's own AuthorityStatus in place (never a new
  // StoredEvent for the raiser), so this is the only signal that tells a
  // live subscriber the item was actually decided.
  async function subscribeToDecisions(): Promise<void> {
    const token = await ensureFollowerToken()
    const introspection = await graphqlQuery<{ __type: { fields: IntrospectedField[] } | null }>(
      config.hostBaseUrl,
      token,
      buildIntrospectionQuery(config.appId, AUTHORITY_DECISION_TYPE),
    )
    const fields = introspection.__type?.fields ?? []
    if (fields.length === 0) return // this AppId has never registered authorityDecision yet

    const query = buildSubscriptionQuery(config.appId, AUTHORITY_DECISION_TYPE, toSubscriptionFieldSelectors(fields), undefined, 'REPLAY', 0)
    const fieldName = subscriptionFieldName(config.appId, AUTHORITY_DECISION_TYPE)

    unsubscribeDecision = graphqlSubscribe<Record<string, Record<string, unknown>>>(
      config.hostBaseUrl,
      token,
      query,
      (data) => {
        const targetEventId = data[fieldName]?.targetEventId as string | undefined
        if (!targetEventId) return
        items.value = items.value.filter((item) => item.eventId !== targetEventId)
      },
      (error) => console.error('Pending-queue decision subscription error', error),
    )
  }

  async function subscribe(): Promise<void> {
    await subscribeToDecisions()
    await subscribeToRaiser()
  }

  function stopSubscription(): void {
    unsubscribeRaiser?.()
    unsubscribeRaiser = null
    unsubscribeDecision?.()
    unsubscribeDecision = null
  }

  // Reuses useEventComposer.publish() as-is -- the exact same Meaning-field/
  // RFC 9470 step-up-and-retry mechanism built for the generic Composer,
  // applied here to a domain-specific decision instead of a domain-agnostic
  // one. authorityDecision's own schema (VitalsSharedTypes/MeridianSharedTypes)
  // is the fixed { targetEventId, decision, decidingActorId, reason } shape.
  async function decide(
    eventId: string,
    decision: 'accepted' | 'rejected',
    decidingActorId: string,
    reason: string,
    meaning: string,
  ): Promise<{ ok: boolean; status?: string; entityId?: string; steppedUp?: boolean }> {
    return decisionComposer.publish(AUTHORITY_DECISION_TYPE, { targetEventId: eventId, decision, decidingActorId, reason }, meaning)
  }

  return { items, subscribe, stopSubscription, decide }
}
