import type { ClientOutboxEntry } from '../types'

// ADR-069's explicit/manual "sneakernet" transfer for a genuinely air-
// gapped device -- reusing ADR-068's portable bundle SHAPE directly
// (manifest line + NDJSON event lines, a manifest hash over an ordered
// list of per-item content hashes), adapted for a queued OUTBOUND
// COMMAND rather than a historical STORED EVENT: a `ClientOutboxEntry`
// has no `ChainHash` at all (it hasn't been published yet, so nothing
// has chained it into the server's own hash chain) -- `contentHash`
// below is this format's own per-entry stand-in, playing the identical
// role `ChainHash` plays in `EventStore.LineageExport`'s bundle: the
// thing whose ordered values the manifest hash is computed over.
export interface OutboxBundleManifest {
  exportedAt: string
  exportedByInstanceId: string
  manifestHash: string
}

export interface OutboxBundleEntry extends ClientOutboxEntry {
  contentHash: string
}

export interface OutboxBundle {
  manifest: OutboxBundleManifest
  entries: OutboxBundleEntry[]
}

export function toNdjson(bundle: OutboxBundle): string {
  return [JSON.stringify(bundle.manifest), ...bundle.entries.map((e) => JSON.stringify(e))].join('\n')
}

export function parseNdjson(ndjson: string): OutboxBundle {
  const lines = ndjson.split('\n').map((l) => l.trim()).filter((l) => l.length > 0)
  if (lines.length === 0) throw new Error('empty outbox bundle')

  const manifest = JSON.parse(lines[0]) as OutboxBundleManifest
  const entries = lines.slice(1).map((line) => JSON.parse(line) as OutboxBundleEntry)
  return { manifest, entries }
}
