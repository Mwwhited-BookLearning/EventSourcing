import { graphqlQuery } from './graphqlClient'

export interface PlaybackResult {
  asOfSequenceNumber: number
  data: string
  extensions: string
  lateArrivalCorrectionShown: boolean
}

// ADR-068's `playbackAsOf` -- VCR-style bitemporal system-time playback,
// folding only events with SequenceNumber <= asOfSequenceNumber in
// ARRIVAL order (the literal opposite of the live Entity Store's valid-
// time-corrected fold, mvvm-client.md's own EntityView). Returns null when
// the entity has no fold at or before that cutoff (docs/features/lineage-
// export-and-playback.md's PlaybackResultNode? is nullable for exactly
// this reason).
export async function playbackAsOf(hostBaseUrl: string, token: string, entityId: string, asOfSequenceNumber: number): Promise<PlaybackResult | null> {
  const query = `query { playbackAsOf(entityId: "${entityId}", asOfSequenceNumber: ${asOfSequenceNumber}) { asOfSequenceNumber data extensions lateArrivalCorrectionShown } }`
  const result = await graphqlQuery<{ playbackAsOf: PlaybackResult | null }>(hostBaseUrl, token, query)
  return result.playbackAsOf
}

// ADR-068's `exportLineage` -- produces a bundleUrl for a lineage-scoped
// export; the bundle itself is downloaded separately from that URL (a
// produced artifact, never stored server-side beyond its retrieval
// window -- LineageExportBundleStore's 15-minute TTL).
export async function exportLineage(hostBaseUrl: string, token: string, entityId: string): Promise<string> {
  const query = `query { exportLineage(entityId: "${entityId}") { bundleUrl } }`
  const result = await graphqlQuery<{ exportLineage: { bundleUrl: string } }>(hostBaseUrl, token, query)
  return result.exportLineage.bundleUrl
}

export async function downloadBundle(hostBaseUrl: string, token: string, bundleUrl: string): Promise<string> {
  const response = await fetch(`${hostBaseUrl}${bundleUrl}`, { headers: { Authorization: `Bearer ${token}` } })
  if (!response.ok) throw new Error(`Failed to download lineage export bundle: HTTP ${response.status}`)
  return response.text()
}
