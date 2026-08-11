import { describe, expect, it } from 'vitest'
import { parseNdjson } from './bundle'

describe('parseNdjson (mirrors EventStore.LineageExport.LineageExportBundle.ParseNdjson)', () => {
  it('parses the manifest line first, then one event per subsequent line', () => {
    const manifest = {
      entityId: 'lab1:Evidence:ev-1',
      eventTypeDefinitionsReferenced: ['lab1/artifactextracted/v1'],
      manifestHash: 'abc123',
      exportedByActorId: 'auditor-3',
      exportedAt: '2026-08-11T10:30:00.123-04:00',
      frameworkVersion: '1.0.0',
      rfc3161Timestamp: null,
    }
    const event = {
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
    }
    const ndjson = [JSON.stringify(manifest), JSON.stringify(event)].join('\n')

    const bundle = parseNdjson(ndjson)
    expect(bundle.manifest).toEqual(manifest)
    expect(bundle.events).toEqual([event])
  })

  it('throws on an empty bundle rather than returning a manifest-less result', () => {
    expect(() => parseNdjson('')).toThrow()
  })

  it('tolerates trailing blank lines, matching the server\'s own RemoveEmptyEntries split', () => {
    const manifest = { entityId: 'x', eventTypeDefinitionsReferenced: [], manifestHash: 'h', exportedByActorId: 'a', exportedAt: '2026-08-11T10:00:00Z', frameworkVersion: '1.0.0', rfc3161Timestamp: null }
    const ndjson = `${JSON.stringify(manifest)}\n\n`
    const bundle = parseNdjson(ndjson)
    expect(bundle.events).toEqual([])
  })
})
