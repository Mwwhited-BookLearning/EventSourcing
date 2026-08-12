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

export function useEventComposer(config: EventComposerConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken
  const clientId = config.composerClientId ?? 'composer-client'
  const clientSecret = config.composerClientSecret ?? 'composer-client-secret'
  let token: string | null = null

  async function ensureComposerToken(): Promise<string> {
    token ??= await tokenFetcher(config.authBaseUrl, clientId, clientSecret, 'events:publish registry:admin')
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

  async function getFormFields(name: string, version: number): Promise<ComposerFormField[]> {
    const currentToken = await ensureComposerToken()
    const result = await graphqlQuery<{ eventType: { jsonSchema: string } | null }>(
      config.hostBaseUrl,
      currentToken,
      `query { eventType(appId: "${config.appId}", name: "${name}", version: ${version}) { jsonSchema } }`,
    )
    if (!result.eventType) return []
    const schema = JSON.parse(result.eventType.jsonSchema) as {
      properties?: Record<string, { type?: string; 'x-masking'?: unknown }>
      required?: string[]
    }
    const required = new Set(schema.required ?? [])
    return Object.entries(schema.properties ?? {}).map(([fieldName, def]) => ({
      name: fieldName,
      type: def.type ?? 'string',
      required: required.has(fieldName),
      editable: !def['x-masking'] && def.type !== 'object',
    }))
  }

  // Deliberately calls the same POST /publish/{eventType} + client-
  // supplied-eventId (ADR-011) mechanism publishCommand already uses for
  // the outbox's own flush -- NOT routed through ClientOutboxEntry itself,
  // since that store's one stanza per instance is scoped to config's own
  // identity, not this composer's second one. Online-only: a failure
  // surfaces directly to the caller rather than being durably queued.
  async function publish(eventType: string, payload: Record<string, unknown>): Promise<{ ok: boolean; status?: string; entityId?: string }> {
    const currentToken = await ensureComposerToken()
    return publishCommand(config.hostBaseUrl, currentToken, {
      commandId: crypto.randomUUID(),
      instanceId: 'composer',
      appId: config.appId,
      eventType,
      entityId: '',
      expectedVersion: null,
      schemaVersion: 1,
      patch: JSON.stringify(payload),
      status: 'Pending',
      enqueuedAt: new Date().toISOString(),
      attempts: 0,
    })
  }

  return { listEventTypes, getFormFields, publish }
}
