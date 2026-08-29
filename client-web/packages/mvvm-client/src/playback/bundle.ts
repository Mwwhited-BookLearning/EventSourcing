// Mirrors EventStore.LineageExport.ExportManifest/ExportedEventLine/
// LineageExportBundle (ADR-068) field-for-field, but NOT their casing --
// LineageExportBundle.ToNdjson() calls System.Text.Json.JsonSerializer.
// Serialize() directly, with no JsonSerializerOptions, which defaults to
// the C# property names verbatim (PascalCase: "EntityId", "ExportedAt",
// ...), not the camelCase ASP.NET's own request-pipeline JSON options
// would normally produce -- this bundle is written by hand, bypassing
// that pipeline entirely. parseNdjson below remaps every top-level key,
// found only by actually parsing a REAL bundle from a live server (a
// bare `JSON.parse(...) as ExportManifest` type assertion here for a
// long time before this fix -- TypeScript is fully satisfied by that
// assertion at compile time; every field read against the mis-cased
// result is `undefined` at runtime instead, silently, until something
// (verifyBundle's own date parsing) finally throws on one).
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

// Shallow PascalCase -> camelCase key remap -- both ExportManifest and
// ExportedEventLine are flat records (EventTypeDefinitionsReferenced is a
// plain string array, no per-element remapping needed), so a one-level
// conversion is all this bundle format ever needs.
function toCamelCaseShallow<T>(source: Record<string, unknown>): T {
  const result: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(source)) {
    result[key.charAt(0).toLowerCase() + key.slice(1)] = value
  }
  return result as T
}

// The server's own ToNdjson()/ParseNdjson() -- manifest line first, one
// event per subsequent line, newline-joined (EventStore.LineageExport.
// LineageExportBundle.cs) -- not a nested JSON array.
export function parseNdjson(ndjson: string): LineageExportBundle {
  const lines = ndjson.split('\n').map((l) => l.trim()).filter((l) => l.length > 0)
  if (lines.length === 0) throw new Error('empty bundle')

  const manifest = toCamelCaseShallow<ExportManifest>(JSON.parse(lines[0]))
  const events = lines.slice(1).map((line) => toCamelCaseShallow<ExportedEventLine>(JSON.parse(line)))
  return { manifest, events }
}
