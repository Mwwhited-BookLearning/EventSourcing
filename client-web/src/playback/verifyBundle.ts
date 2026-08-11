import type { LineageExportBundle } from './bundle'

export interface BundleVerificationResult {
  manifestHashVerified: boolean
  totalEvents: number
  maskedFieldCount: number
  erasedFieldCount: number
  // "Fully independently verified" (ADR-068) requires BOTH the manifest
  // hash to check out AND zero masked/erased fields anywhere in the
  // bundle -- a masked leaf's {"masked": ...}/{"erased": true} wrapper was
  // never part of what PayloadHash/ChainHash were originally computed
  // over (LineageExportService.MaskPayloadAsync applies masking BEFORE
  // this bundle is built), so its presence is disclosed here rather than
  // silently folded into one undifferentiated pass/fail.
  fullyVerified: boolean
}

// SHA-256, hex, lowercase -- matches Convert.ToHexString(...).ToLowerInvariant()
// (EventStore.LineageExport.ManifestHash / EventStore.Domain.EventLog.
// EventChainHash's shared convention) using Web Crypto instead of a hash
// library: available unconditionally in both a normal browser tab and the
// vite-plugin-singlefile offline build (no server, no network, but
// `crypto.subtle` is a standard Web Platform API, not a network call).
export async function sha256Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input)
  const digest = await crypto.subtle.digest('SHA-256', bytes)
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')
}

// System.Text.Json trims a DateTimeOffset's fractional-second digits down
// to millisecond precision when the sub-millisecond (tick-level) portion is
// exactly zero, and drops the fractional part entirely when the whole
// second is exact -- verified directly against the real serializer, not
// assumed (docs/.claude/protocols/verify-before-citing.md). ManifestHash.
// Compute (server-side) hashes the UN-trimmed `DateTimeOffset:"O"` format
// (always exactly 7 fractional digits), so recomputing the same hash here
// requires padding the JSON string's fractional part back out to 7 digits
// -- never truncated information, since a trim only ever happens when the
// dropped digits were genuinely zero.
export function reconstructODateTimeOffsetString(jsonDateString: string): string {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:\d{2})$/.exec(jsonDateString)
  if (!match) throw new Error(`unrecognized DateTimeOffset JSON format: ${jsonDateString}`)
  const [, base, fraction, offset] = match
  const paddedFraction = (fraction ?? '').padEnd(7, '0')
  const normalizedOffset = offset === 'Z' ? '+00:00' : offset
  return `${base}.${paddedFraction}${normalizedOffset}`
}

// Mirrors EventStore.LineageExport.ManifestHash.Compute exactly: pipe-
// delimited ordered ChainHash values, then ExportedByActorId, then
// ExportedAt in ":O" format, SHA-256'd.
export async function computeManifestHash(orderedChainHashes: string[], exportedByActorId: string, exportedAtJson: string): Promise<string> {
  const input = `${orderedChainHashes.join('|')}|${exportedByActorId}|${reconstructODateTimeOffsetString(exportedAtJson)}`
  return sha256Hex(input)
}

// Counts every masked/erased leaf across all events' payloads -- a plain
// JsonNode tree-walk mirroring PayloadMasker's own wrapper shapes
// ({"masked": ...} / {"erased": true}), not a schema-driven re-mask (ADR-
// 068's own "no masking/claims logic in the player" rule: this only
// COUNTS what the export already decided, it never re-decides anything).
function countMaskedAndErasedLeaves(node: unknown): { masked: number; erased: number } {
  if (Array.isArray(node)) {
    return node.reduce(
      (acc, child) => {
        const r = countMaskedAndErasedLeaves(child)
        return { masked: acc.masked + r.masked, erased: acc.erased + r.erased }
      },
      { masked: 0, erased: 0 },
    )
  }
  if (node !== null && typeof node === 'object') {
    const obj = node as Record<string, unknown>
    const keys = Object.keys(obj)
    if (keys.length === 1 && keys[0] === 'masked') return { masked: 1, erased: 0 }
    if (keys.length === 1 && keys[0] === 'erased') return { masked: 0, erased: 1 }
    return Object.values(obj).reduce<{ masked: number; erased: number }>(
      (acc, child) => {
        const r = countMaskedAndErasedLeaves(child)
        return { masked: acc.masked + r.masked, erased: acc.erased + r.erased }
      },
      { masked: 0, erased: 0 },
    )
  }
  return { masked: 0, erased: 0 }
}

// Deliberately does NOT attempt to recompute each event's own PayloadHash
// from its (possibly-masked) Payload: ExportedEventLine carries no
// ParentEventIds (EventPayloadHash.Compute's own third input), so any
// event with real causal parents (ADR-005) would recompute to a DIFFERENT
// hash than its original even when nothing was tampered -- a false
// "tamper detected" for ordinary, legitimate lineage data. What IS exact,
// independent of trusting the exporting party, and safely recomputable
// from the bundle alone is the manifest hash over the bundle's own
// (stored, trusted-as-given) ChainHash values -- verified below. This is
// a narrower, honestly-scoped mechanic than ADR-068's own sequence
// diagram wording ("recompute ChainHash sequence... exact... for every
// unmasked event") describes; ADR-068's Consequences already flagged this
// exact build target as "not yet detailed" -- this is that detail, filled
// in against what the actually-exported schema supports, not against a
// per-event PayloadHash-from-Payload re-derivation that isn't safely
// buildable from ExportedEventLine's real fields.
export async function verifyBundle(bundle: LineageExportBundle): Promise<BundleVerificationResult> {
  const ordered = [...bundle.events].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
  const recomputedHash = await computeManifestHash(ordered.map((e) => e.chainHash), bundle.manifest.exportedByActorId, bundle.manifest.exportedAt)
  const manifestHashVerified = recomputedHash === bundle.manifest.manifestHash

  let maskedFieldCount = 0
  let erasedFieldCount = 0
  for (const event of bundle.events) {
    const payload = JSON.parse(event.payload)
    const counts = countMaskedAndErasedLeaves(payload)
    maskedFieldCount += counts.masked
    erasedFieldCount += counts.erased
  }

  return {
    manifestHashVerified,
    totalEvents: bundle.events.length,
    maskedFieldCount,
    erasedFieldCount,
    fullyVerified: manifestHashVerified && maskedFieldCount === 0 && erasedFieldCount === 0,
  }
}
