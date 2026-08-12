// Mirrors EventStore.LineageExport.ExportManifest/ExportedEventLine/
// LineageExportBundle (ADR-068) exactly -- same field names/casing as the
// server's System.Text.Json output, so ParseNdjson round-trips a bundle
// produced by the real /lineage-exports/{id} endpoint with no translation
// layer in between.
export interface ExportManifest {
  entityId: string
  eventTypeDefinitionsReferenced: string[]
  manifestHash: string
  exportedByActorId: string
  exportedAt: string
  frameworkVersion: string
  rfc3161Timestamp: string | null
}

export interface ExportedEventLine {
  eventId: string
  appId: string
  entityId: string
  eventType: string
  schemaVersion: number
  sequenceNumber: number
  chainHash: string
  payloadHash: string
  payload: string
  occurredAt: string
  lateArrivalFlag: boolean
}

export interface LineageExportBundle {
  manifest: ExportManifest
  events: ExportedEventLine[]
}

// The server's own ToNdjson()/ParseNdjson() -- manifest line first, one
// event per subsequent line, newline-joined (EventStore.LineageExport.
// LineageExportBundle.cs) -- not a nested JSON array.
export function parseNdjson(ndjson: string): LineageExportBundle {
  const lines = ndjson.split('\n').map((l) => l.trim()).filter((l) => l.length > 0)
  if (lines.length === 0) throw new Error('empty bundle')

  const manifest = JSON.parse(lines[0]) as ExportManifest
  const events = lines.slice(1).map((line) => JSON.parse(line) as ExportedEventLine)
  return { manifest, events }
}
