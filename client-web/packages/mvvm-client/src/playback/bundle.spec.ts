import { describe, expect, it } from 'vitest'
import { parseNdjson } from './bundle'

describe('parseNdjson (mirrors EventStore.LineageExport.LineageExportBundle.ParseNdjson)', () => {
  // The real server shape: LineageExportBundle.ToNdjson() calls
  // System.Text.Json.JsonSerializer.Serialize() with no options, which
  // defaults to PascalCase (the C# property names verbatim), not
  // camelCase -- these mocks originally used camelCase, matching a bug
  // in parseNdjson's own prior "as ExportManifest" type assertion
  // rather than reality. Found only by actually parsing a real bundle
  // from a live server (VitalsWorkflowCLineageExportAndPlaybackPlaybookTests),
  // where every field read against the mis-cased result was silently
  // `undefined` until verifyBundle's own date parsing finally threw.
  it('parses the manifest line first, then one event per subsequent line, remapping the server\'s own PascalCase keys', () => {
    const serverManifest = {
      EntityId: 'lab1:Evidence:ev-1',
      EventTypeDefinitionsReferenced: ['lab1/artifactextracted/v1'],
      ManifestHash: 'abc123',
      ExportedByActorId: 'auditor-3',
      ExportedAt: '2026-08-11T10:30:00.123-04:00',
      FrameworkVersion: '1.0.0',
      Rfc3161Timestamp: null,
    }
    const serverEvent = {
      EventId: 'event-1',
      AppId: 'lab1',
      EntityId: 'lab1:Evidence:ev-1',
      EventType: 'ArtifactExtracted',
      SchemaVersion: 1,
      SequenceNumber: 1,
      ChainHash: 'chain-1',
      PayloadHash: 'hash-1',
      Payload: '{"a":1}',
      OccurredAt: '2026-08-11T10:00:00-04:00',
      LateArrivalFlag: false,
    }
    const ndjson = [JSON.stringify(serverManifest), JSON.stringify(serverEvent)].join('\n')

    const bundle = parseNdjson(ndjson)
    expect(bundle.manifest).toEqual({
      entityId: 'lab1:Evidence:ev-1',
      eventTypeDefinitionsReferenced: ['lab1/artifactextracted/v1'],
      manifestHash: 'abc123',
      exportedByActorId: 'auditor-3',
      exportedAt: '2026-08-11T10:30:00.123-04:00',
      frameworkVersion: '1.0.0',
      rfc3161Timestamp: null,
    })
    expect(bundle.events).toEqual([
      {
        eventId: 'event-1',
        appId: 'lab1',
        entityId: 'lab1:Evidence:ev-1',
        eventType: 'ArtifactExtracted',
        schemaVersion: 1,
        sequenceNumber: 1,
        chainHash: 'chain-1',
        payloadHash: 'hash-1',
        payload: '{"a":1}',
        occurredAt: '2026-08-11T10:00:00-04:00',
        lateArrivalFlag: false,
      },
    ])
  })

  it('throws on an empty bundle rather than returning a manifest-less result', () => {
    expect(() => parseNdjson('')).toThrow()
  })

  it('tolerates trailing blank lines, matching the server\'s own RemoveEmptyEntries split', () => {
    const manifest = { EntityId: 'x', EventTypeDefinitionsReferenced: [], ManifestHash: 'h', ExportedByActorId: 'a', ExportedAt: '2026-08-11T10:00:00Z', FrameworkVersion: '1.0.0', Rfc3161Timestamp: null }
    const ndjson = `${JSON.stringify(manifest)}\n\n`
    const bundle = parseNdjson(ndjson)
    expect(bundle.events).toEqual([])
  })
})
