import { describe, expect, it } from 'vitest'
import { computeManifestHash, reconstructODateTimeOffsetString, verifyBundle } from './verifyBundle'
import type { LineageExportBundle } from './bundle'

describe('reconstructODateTimeOffsetString (must exactly reproduce DateTimeOffset "O" format from System.Text.Json output)', () => {
  it('pads a millisecond-trimmed fraction back out to 7 digits', () => {
    expect(reconstructODateTimeOffsetString('2026-08-11T10:30:00.123-04:00')).toBe('2026-08-11T10:30:00.1230000-04:00')
  })

  it('leaves an already-7-digit fraction untouched', () => {
    expect(reconstructODateTimeOffsetString('2026-08-11T10:30:00.1234567-04:00')).toBe('2026-08-11T10:30:00.1234567-04:00')
  })

  it('fills in an entirely-absent fraction (a whole-second timestamp) with 7 zeros', () => {
    expect(reconstructODateTimeOffsetString('2026-08-11T10:30:00-04:00')).toBe('2026-08-11T10:30:00.0000000-04:00')
  })

  it('normalizes a "Z" offset to the explicit "+00:00" DateTimeOffset:"O" always emits', () => {
    expect(reconstructODateTimeOffsetString('2026-08-11T10:30:00.123Z')).toBe('2026-08-11T10:30:00.1230000+00:00')
  })
})

describe('computeManifestHash (mirrors EventStore.LineageExport.ManifestHash.Compute)', () => {
  it('reproduces the exact hash the real C# implementation computes for the same inputs', async () => {
    // Cross-checked directly against System.Text.Cryptography.SHA256/
    // DateTimeOffset.ToString("O") this session, not merely asserted --
    // see docs/changes/2026-08-11.md.
    const hash = await computeManifestHash(['aaa111', 'bbb222'], 'auditor-3', '2026-08-11T10:30:00.123-04:00')
    expect(hash).toBe('30025f65496ef69e02ba76f00eef93b0c71d58bcc0e88aa2595d9eb44922b6a5')
  })
})

function makeBundle(overrides: Partial<LineageExportBundle['manifest']> = {}, payloads: string[] = ['{"a":1}', '{"b":2}']): LineageExportBundle {
  const events = payloads.map((payload, i) => ({
    eventId: `event-${i}`,
    appId: 'lab1',
    entityId: 'lab1:Evidence:ev-1',
    eventType: 'ArtifactExtracted',
    schemaVersion: 1,
    sequenceNumber: i + 1,
    chainHash: `chain-${i}`,
    payloadHash: `hash-${i}`,
    payload,
    occurredAt: '2026-08-11T10:00:00-04:00',
    lateArrivalFlag: false,
  }))
  return {
    manifest: {
      entityId: 'lab1:Evidence:ev-1',
      eventTypeDefinitionsReferenced: ['lab1/artifactextracted/v1'],
      manifestHash: '',
      exportedByActorId: 'auditor-3',
      exportedAt: '2026-08-11T10:30:00.123-04:00',
      frameworkVersion: '1.0.0',
      rfc3161Timestamp: null,
      ...overrides,
    },
    events,
  }
}

describe('verifyBundle', () => {
  it('reports fully verified when the manifest hash matches and nothing is masked or erased', async () => {
    const bundle = makeBundle()
    bundle.manifest.manifestHash = await computeManifestHash(
      bundle.events.map((e) => e.chainHash),
      bundle.manifest.exportedByActorId,
      bundle.manifest.exportedAt,
    )
    const result = await verifyBundle(bundle)
    expect(result.manifestHashVerified).toBe(true)
    expect(result.maskedFieldCount).toBe(0)
    expect(result.erasedFieldCount).toBe(0)
    expect(result.fullyVerified).toBe(true)
  })

  it('reports "verified except N masked fields" -- distinct from an undifferentiated pass/fail -- when a field was masked', async () => {
    const bundle = makeBundle({}, ['{"a":1,"sourcePath":{"masked":"***"}}', '{"b":2}'])
    bundle.manifest.manifestHash = await computeManifestHash(
      bundle.events.map((e) => e.chainHash),
      bundle.manifest.exportedByActorId,
      bundle.manifest.exportedAt,
    )
    const result = await verifyBundle(bundle)
    expect(result.manifestHashVerified).toBe(true)
    expect(result.maskedFieldCount).toBe(1)
    expect(result.fullyVerified).toBe(false)
  })

  it('counts an erased field separately from a merely-masked one', async () => {
    const bundle = makeBundle({}, ['{"a":{"erased":true}}'])
    bundle.manifest.manifestHash = await computeManifestHash(
      bundle.events.map((e) => e.chainHash),
      bundle.manifest.exportedByActorId,
      bundle.manifest.exportedAt,
    )
    const result = await verifyBundle(bundle)
    expect(result.erasedFieldCount).toBe(1)
    expect(result.maskedFieldCount).toBe(0)
  })

  it('detects tampering: a manifest hash that does not match a recomputation over the bundle\'s own ChainHash values fails verification', async () => {
    const bundle = makeBundle({ manifestHash: 'not-the-real-hash' })
    const result = await verifyBundle(bundle)
    expect(result.manifestHashVerified).toBe(false)
    expect(result.fullyVerified).toBe(false)
  })

  it('recomputes over ChainHash values in SequenceNumber order regardless of the array\'s own order in the bundle', async () => {
    const bundle = makeBundle()
    bundle.manifest.manifestHash = await computeManifestHash(
      bundle.events.map((e) => e.chainHash), // already SequenceNumber order
      bundle.manifest.exportedByActorId,
      bundle.manifest.exportedAt,
    )
    bundle.events.reverse() // NDJSON order is not contractually guaranteed to survive client-side handling
    const result = await verifyBundle(bundle)
    expect(result.manifestHashVerified).toBe(true)
  })
})
