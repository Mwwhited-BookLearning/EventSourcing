import { fetchToken } from '../api/authClient'
import { graphqlQuery } from '../api/graphqlClient'
import { publishCommand } from '../api/publishClient'
import type { FetchTokenFn } from './useEntityViewActions'

// docs/features/mvvm-client.md, "Event Composer" -- a SECOND identity from
// this instance's own read-only config.clientId, deliberately: listing/
// introspecting schemas needs registry:admin (RegistryQueries.cs), a scope
// no ordinary browsing identity (follower-client, events:follow only)
// should hold. composer-client (EventStore.DevIdp/DevIdpSeeder.cs) holds
// both registry:admin and events:publish -- the same both-scopes-in-one-
// caller posture telemetry-client/attachments-client/peer-sync-client
// already establish elsewhere in this codebase.
export interface EventComposerConfig {
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
  composerClientId?: string
  composerClientSecret?: string
  // "Domain Decision Queues" -- composer-client's own identity genuinely
  // needs both scopes (it lists/introspects AND publishes), but a
  // decision-queue caller (vitals-pi-client/meridian-analyst-client) only
  // ever publishes an already-known "authorityDecision" -- granting it
  // registry:admin too would be unnecessary over-privilege for a
  // clinical/compliance-actor identity. Defaults to the original
  // composer-client scope so every existing caller is unaffected.
  scope?: string
}

export interface EventTypeSummary {
  name: string
  version: number
  entityType: string
  isActive: boolean
}

export interface ComposerFormField {
  name: string
  type: string // string | number | integer | boolean | array
  required: boolean
  // A masked (x-masking) or nested (object) property is never rendered as
  // an editable input -- shown informational-only instead, the same
  // "never fail to render, degrade instead" posture the generic fallback
  // view already establishes for reading an entity, applied here to
  // composing one.
  editable: boolean
}

// ADR-066 -- null when the event type has no RequiredSignature configured
// (the common, unaffected case); present names the RFC 9470 acr_values/
// max_age the Composer must satisfy before Publish-time enforcement
// (PublishService.StepUpSatisfied) will accept the publish.
export interface ComposerRequiredSignature {
  acrValues: string[]
  maxAge: number | null
}

export interface ComposerEventTypeDetail {
  fields: ComposerFormField[]
  requiredSignature: ComposerRequiredSignature | null
}

export function useEventComposer(config: EventComposerConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken
  const clientId = config.composerClientId ?? 'composer-client'
  const clientSecret = config.composerClientSecret ?? 'composer-client-secret'
  const scope = config.scope ?? 'events:publish registry:admin'
  let token: string | null = null

  async function ensureComposerToken(): Promise<string> {
    token ??= await tokenFetcher(config.authBaseUrl, clientId, clientSecret, scope)
    return token
  }

  // ListAsync (EventStore.SchemaRegistry) deliberately returns EVERY
  // historical version, for its own original browse/paginate-history use
  // case -- filtered here to just the active one per name, since the
  // Composer only ever wants "the current shape to publish against," not
  // a version-history browser. Found live: a schema type re-registered
  // across several AppHost restarts (this repo's own dev-iteration
  // pattern, no true registration idempotency) otherwise floods this
  // dropdown with near-duplicate stale entries.
  async function listEventTypes(): Promise<EventTypeSummary[]> {
    const currentToken = await ensureComposerToken()
    const result = await graphqlQuery<{ eventTypes: EventTypeSummary[] }>(
      config.hostBaseUrl,
      currentToken,
      `query { eventTypes(appId: "${config.appId}") { name version entityType isActive } }`,
    )
    return (result.eventTypes ?? []).filter((et) => et.isActive)
  }

  // `requiredSignature` is EventTypeDefinition.RequiredSignature (ADR-066),
  // exposed automatically by HotChocolate off the same domain object
  // RegistryQueries.GetEventTypeAsync already returns -- no new resolver
  // needed. null for the ordinary, unaffected case (no sign-off configured).
  async function getEventTypeDetail(name: string, version: number): Promise<ComposerEventTypeDetail> {
    const currentToken = await ensureComposerToken()
    const result = await graphqlQuery<{
      eventType: { jsonSchema: string; requiredSignature: { acrValues: string[]; maxAge: number | null } | null } | null
    }>(
      config.hostBaseUrl,
      currentToken,
      `query { eventType(appId: "${config.appId}", name: "${name}", version: ${version}) { jsonSchema requiredSignature { acrValues maxAge } } }`,
    )
    if (!result.eventType) return { fields: [], requiredSignature: null }
    const schema = JSON.parse(result.eventType.jsonSchema) as {
      properties?: Record<string, { type?: string; 'x-masking'?: unknown }>
      required?: string[]
    }
    const required = new Set(schema.required ?? [])
    const fields = Object.entries(schema.properties ?? {}).map(([fieldName, def]) => ({
      name: fieldName,
      type: def.type ?? 'string',
      required: required.has(fieldName),
      editable: !def['x-masking'] && def.type !== 'object',
    }))
    return { fields, requiredSignature: result.eventType.requiredSignature }
  }

  // Deliberately calls the same POST /publish/{eventType} + client-
  // supplied-eventId (ADR-011) mechanism publishCommand already uses for
  // the outbox's own flush -- NOT routed through ClientOutboxEntry itself,
  // since that store's one stanza per instance is scoped to config's own
  // identity, not this composer's second one. Online-only: a failure
  // surfaces directly to the caller rather than being durably queued.
  //
  // ADR-066/RFC 9470 -- a first attempt against a RequiredSignature-
  // configured event type, with the composer's ordinary token (no `acr`
  // claim), gets the 401 challenge publishClient.ts now surfaces as
  // `stepUpRequired`. This is where the client "redirects the caller
  // through the IdP to step up... and retries with the resulting token"
  // (the ADR's own Decision text) -- dev-simulated (no real interactive
  // re-auth exists here, see authClient.ts's own comment), one retry only,
  // never a loop: a second stepUpRequired on the retry is a real, distinct
  // failure (e.g. the IdP itself would refuse this acr), reported as-is
  // rather than retried again.
  async function publish(eventType: string, payload: Record<string, unknown>, meaning?: string): Promise<{ ok: boolean; status?: string; entityId?: string; permanentFailure?: boolean; steppedUp?: boolean }> {
    const entry = {
      commandId: crypto.randomUUID(),
      instanceId: 'composer',
      appId: config.appId,
      eventType,
      entityId: '',
      expectedVersion: null,
      schemaVersion: 1,
      patch: JSON.stringify(payload),
      status: 'Pending' as const,
      enqueuedAt: new Date().toISOString(),
      attempts: 0,
      meaning,
    }
    const currentToken = await ensureComposerToken()
    const result = await publishCommand(config.hostBaseUrl, currentToken, entry)
    if (!result.stepUpRequired) return result

    token = await tokenFetcher(config.authBaseUrl, clientId, clientSecret, scope, result.stepUpRequired.acrValues[0])
    const retried = await publishCommand(config.hostBaseUrl, token, entry)
    return { ...retried, steppedUp: !retried.stepUpRequired }
  }

  return { listEventTypes, getEventTypeDetail, publish }
}

// A read-only sibling to the Composer's own schema introspection, for ANY
// caller that needs to re-case a payload's own field names to a schema's
// REAL declared casing before publishing -- not just the Composer UI
// itself. EventTypeSchemaReader/GraphQL always exposes a schema's fields
// camelCased regardless of how the schema itself spelled them (confirmed
// against every schema in this repo, e.g. "CheckId" resolves as `checkId`),
// so a caller re-publishing data it only ever saw through that camelCased
// lens (e.g. useEntityViewActions' own cached entity data,
// App.vue.submitAmountCommand) needs this mapping to satisfy a PascalCase
// schema's own `required` check, which is an exact, case-sensitive string
// match -- found while building OfflineOutboxSyncPlaybookTests.cs
// (TODO.md), where App.vue's generic "Dispatch a command" panel had never
// once successfully published against a real Vitals/Meridian schema for
// exactly this reason. Uses composer-client's identity (registry:admin)
// purely to READ the schema shape -- never to publish; returns an empty
// map (not a throw) if the event type isn't found or introspection itself
// fails, so a caller can fall back to sending its patch un-recased rather
// than blocking the dispatch entirely.
export async function resolveEventTypeFieldCasing(
  config: Pick<EventComposerConfig, 'hostBaseUrl' | 'authBaseUrl' | 'appId' | 'composerClientId' | 'composerClientSecret'>,
  eventType: string,
  deps: { fetchToken?: FetchTokenFn } = {},
): Promise<Map<string, string>> {
  try {
    const composer = useEventComposer(config, deps)
    const types = await composer.listEventTypes()
    const match = types.find((t) => t.name.toLowerCase() === eventType.toLowerCase())
    if (!match) return new Map()
    const detail = await composer.getEventTypeDetail(match.name, match.version)
    return new Map(detail.fields.map((f) => [f.name.toLowerCase(), f.name]))
  } catch {
    // Best-effort -- a caller falls back to sending its patch un-recased
    // rather than blocking the dispatch entirely (e.g. composer-client
    // unreachable/misconfigured for this deployment).
    return new Map()
  }
}
