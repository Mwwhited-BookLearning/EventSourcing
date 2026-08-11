// Shapes mirror docs/features/mvvm-client.md's own ER diagram exactly
// (ClientOutboxEntry, ClientEntityCacheEntry) -- these are CLIENT-LOCAL
// (IndexedDB) structures, never persisted server-side, so they have no C#
// counterpart the way ViewDefinition does.

export type OutboxEntryStatus = 'Pending' | 'Delivered' | 'Failed'

export interface ClientOutboxEntry {
  commandId: string // uuid -- doubles as the EventId the server's Idempotent Receiver (ADR-011) dedups on
  instanceId: string // namespaces entries per client instance/subscription target (ADR-039)
  appId: string
  eventType: string
  entityId: string
  expectedVersion: number | null
  schemaVersion: number
  patch: string // JSON payload -- Optional<T>-wrapped semantics live server-side (ADR-022); here it's just the JSON body /publish/{eventType} expects
  status: OutboxEntryStatus
  enqueuedAt: string
  attempts: number
  // ADR-070 -- "captured readings feed the existing durable outbox
  // unchanged" (the queue/flush/durability mechanics genuinely are
  // unchanged) but a per-integration schema choice means an entry may
  // need to flush to a DIFFERENT destination than an ordinary publish:
  // absent/undefined (every entry before this item, and every ordinary
  // command) means "publish," the original and only behavior; the
  // 'streamingSample' branch below is the sole addition. `channelId`/
  // `monotonicElapsedMicros` are populated only in that branch --
  // ordinary commands never set them, `patch` still carries the raw
  // reading value as its JSON body either way.
  deliveryKind?: 'streamingSample'
  channelId?: string
  monotonicElapsedMicros?: number
  // ADR-070/035/036 -- both branches of "a device-sourced reading
  // defaults to non-authoritative unless the device carries a self-
  // attested identity" (ADR-070's own Decision text), realized via
  // PublishService's two EXISTING, distinct ADR-042 triggers (unchanged
  // here, only threaded through from the client for the first time):
  // `reviewPending` (a raw, un-reviewed CONTENT/confidence case -- no
  // identity claim at all, the honest default for a reading nobody has
  // attested to) vs. `attestedActorId`/`attestedClaims` (an IDENTITY
  // claim -- set instead of `reviewPending` when the specific device
  // carries a real self-attested DID/UCAN credential, per
  // deviceReadingOutbox.ts's own DiscreteMapping). Only meaningful for a
  // 'publish'-bound (discrete) entry -- a streamingSample entry has no
  // AuthorityStatus concept at all (TelemetrySample carries no such
  // field).
  reviewPending?: boolean
  attestedActorId?: string
  attestedClaims?: Record<string, unknown>
}

export interface ClientEntityCacheEntry {
  entityId: string
  instanceId: string
  entityType: string
  data: Record<string, unknown>
  extensions: Record<string, unknown>
  schemaVersion: number
  conflictFlag: boolean
  lateArrivalFlag: boolean
  authorityStatus: string
  cachedAt: string
}

export interface ViewDefinitionCacheEntry {
  entityType: string
  viewKind: string
  version: number
  templateContent: string
  cachedAt: string
}

// The server's own registered-property shape a subscription payload's
// per-schema fields flow through -- everything that isn't one of the four
// fixed envelope fields FollowSubscriptionTypeModule.BuildEnvelopeFlagFields
// always adds (ADR-024/029/035, "Compatibility & Deployment Discipline").
export interface FollowedEventEnvelope {
  conflictFlag: boolean
  lateArrivalFlag: boolean
  authorityStatus: string
  schemaVersion: number
  [fieldName: string]: unknown
}
