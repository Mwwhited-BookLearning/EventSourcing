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
