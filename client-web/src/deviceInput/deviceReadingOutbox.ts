import type { ClientOutboxEntry } from '../types'
import type { DeviceReading } from './types'

// ADR-070's own explicit statement: "server-side mapping is a per-
// integration schema choice, not a framework-wide rule" -- which branch
// applies is configured per integration, never inferred from the reading
// itself. A continuous mapping has no `appId`/`eventType`/`entityId` at
// all (it isn't a StoredEvent); a discrete mapping has no `channelId`
// (it isn't a TelemetrySample) -- the discriminated union makes mixing
// the two fields impossible to construct, not just a runtime check.
export interface DiscreteMapping {
  kind: 'discrete'
  appId: string
  eventType: string
  entityId: string
  schemaVersion?: number
  // ADR-070/036 -- present only when the specific physical device
  // carries a real self-attested DID/UCAN identity; absent (the common
  // case) means the reading carries no identity claim of its own, and
  // toOutboxEntry below marks it reviewPending instead (ADR-042's
  // content/confidence trigger, the honest default for a raw,
  // un-reviewed device reading -- see that function's own comment for
  // why this is the trigger used, not attestedActorId/attestedClaims).
  deviceAttestation?: { actorId: string; claims: Record<string, unknown> }
}

export interface ContinuousMapping {
  kind: 'continuous'
  channelId: string
}

export type ReadingMapping = DiscreteMapping | ContinuousMapping

// Builds the ClientOutboxEntry a captured reading becomes -- the ONE
// mechanism every IDeviceInputSource adapter's captured reading feeds,
// per ADR-070's "no new local-storage mechanism" rule. Enqueuing itself
// (durability, IndexedDB) is the caller's job via the ordinary
// `useOutboxStore.enqueue`, exactly as any other command -- this
// function only shapes the entry, it doesn't perform the write.
export function toOutboxEntry(instanceId: string, reading: DeviceReading, mapping: ReadingMapping): ClientOutboxEntry {
  const base = {
    commandId: crypto.randomUUID(),
    instanceId,
    expectedVersion: null,
    status: 'Pending' as const,
    enqueuedAt: reading.timestamp,
    attempts: 0,
  }

  if (mapping.kind === 'discrete') {
    return {
      ...base,
      appId: mapping.appId,
      eventType: mapping.eventType,
      entityId: mapping.entityId,
      schemaVersion: mapping.schemaVersion ?? 1,
      patch: JSON.stringify(reading.value),
      // Exactly one of these two ADR-042 triggers fires -- never both,
      // matching PublishService's own mutually-exclusive precedence
      // (identity claim checked before content/confidence). A device
      // with a real self-attested identity carries THAT through
      // (AuthorityStatus starts "unattested" via the identity path); the
      // ordinary, unattested-device default instead uses
      // `reviewPending` (AuthorityStatus starts "pending_review" via the
      // content path) -- ADR-070's own "defaults to non-authoritative
      // unless the device attests" distinction, realized as two
      // genuinely different starting states, not the same one twice.
      ...(mapping.deviceAttestation
        ? { attestedActorId: mapping.deviceAttestation.actorId, attestedClaims: mapping.deviceAttestation.claims }
        : { reviewPending: true }),
    }
  }

  return {
    ...base,
    appId: '',
    eventType: '',
    entityId: '',
    schemaVersion: 1,
    patch: JSON.stringify(reading.value),
    deliveryKind: 'streamingSample',
    channelId: mapping.channelId,
    monotonicElapsedMicros: reading.monotonicElapsedMicros,
  }
}
