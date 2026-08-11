import type { ClientOutboxEntry } from '../types'
import { computeManifestHash, sha256Hex } from '../playback/verifyBundle'
import { parseNdjson, toNdjson, type OutboxBundle, type OutboxBundleEntry } from './bundle'

// The per-entry content hash a queued command's own manifest is computed
// over -- deliberately over the SAME fields the server's Idempotent
// Receiver (ADR-011) treats as this command's identity/content, so two
// exports of the same still-pending entry (never redelivered in between)
// produce the same hash, the same "recomputing this hash independently
// re-derives it" property `ManifestHash`/`ChainHash` already have
// server-side.
async function computeContentHash(entry: ClientOutboxEntry): Promise<string> {
  const canonical = JSON.stringify({
    commandId: entry.commandId, appId: entry.appId, eventType: entry.eventType, entityId: entry.entityId,
    expectedVersion: entry.expectedVersion, schemaVersion: entry.schemaVersion, patch: entry.patch,
  })
  return sha256Hex(canonical)
}

// ADR-069 -- for a device with no network path at all, ever: export every
// still-Pending queued command to a portable bundle for physical
// transport. Delivered/Failed entries are excluded -- nothing left to
// carry to another system to apply.
export async function exportOutboxBundle(entries: ClientOutboxEntry[], exportedByInstanceId: string): Promise<string> {
  const pending = entries.filter((e) => e.status === 'Pending')
  const bundleEntries: OutboxBundleEntry[] = await Promise.all(
    pending.map(async (entry) => ({ ...entry, contentHash: await computeContentHash(entry) })),
  )
  const exportedAt = new Date().toISOString()
  const manifestHash = await computeManifestHash(bundleEntries.map((e) => e.contentHash), exportedByInstanceId, exportedAt)
  return toNdjson({ manifest: { exportedAt, exportedByInstanceId, manifestHash }, entries: bundleEntries })
}

export interface OutboxImportResult {
  verified: boolean
  entries: ClientOutboxEntry[]
}

// Verifies the bundle is complete and unaltered BEFORE returning anything
// importable -- the same "reject before any write" discipline
// EventStore.LineageExport.LineageExportService.ImportAsync already
// established server-side for the read-side counterpart of this format.
// The receiving system's own outbox store applies each returned entry
// via its ordinary enqueue path, so re-importing an already-delivered
// bundle is safe: ADR-011's idempotency on `commandId` makes a duplicate
// enqueue-and-flush a no-op at the server, never a double-apply.
export async function importOutboxBundle(ndjson: string): Promise<OutboxImportResult> {
  const bundle: OutboxBundle = parseNdjson(ndjson)
  const recomputedManifestHash = await computeManifestHash(
    bundle.entries.map((e) => e.contentHash),
    bundle.manifest.exportedByInstanceId,
    bundle.manifest.exportedAt,
  )
  if (recomputedManifestHash !== bundle.manifest.manifestHash) return { verified: false, entries: [] }

  // The manifest hash alone only proves the LIST of contentHash values
  // wasn't altered -- it says nothing about whether a given entry's own
  // fields still match ITS OWN carried contentHash. Re-deriving each
  // entry's hash from its own current content and comparing closes that
  // gap, the same "recompute, don't trust the stored value" discipline
  // `EventStore.Domain.EventLog.ChainVerificationService` already applies
  // server-side (re-deriving `PayloadHash` from `Payload`, not trusting
  // the column blindly) -- found by writing an adversarial "tamper the
  // payload but leave contentHash alone" test against this exact gap.
  const recomputedContentHashes = await Promise.all(bundle.entries.map((e) => computeContentHash(e)));
  const anyEntryTampered = bundle.entries.some((entry, i) => entry.contentHash !== recomputedContentHashes[i])
  if (anyEntryTampered) return { verified: false, entries: [] }

  return { verified: true, entries: bundle.entries.map(({ contentHash: _contentHash, ...entry }) => entry) }
}
