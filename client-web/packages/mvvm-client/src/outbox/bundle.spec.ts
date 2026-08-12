import { describe, expect, it } from 'vitest'
import { parseNdjson, toNdjson, type OutboxBundle } from './bundle'

describe('outbox bundle NDJSON (ADR-069, reusing ADR-068\'s portable format)', () => {
  it('round-trips a manifest and entries through toNdjson/parseNdjson', () => {
    const bundle: OutboxBundle = {
      manifest: { exportedAt: '2026-08-11T10:00:00.000Z', exportedByInstanceId: 'instance-a', manifestHash: 'abc123' },
      entries: [
        {
          commandId: 'cmd-1', instanceId: 'instance-a', appId: 'mvvm-demo', eventType: 'OrderPlaced', entityId: 'mvvm-demo:orderplaced:o-1',
          expectedVersion: null, schemaVersion: 1, patch: '{"Amount":175}', status: 'Pending', enqueuedAt: '2026-08-11T09:00:00.000Z', attempts: 0,
          contentHash: 'hash-1',
        },
      ],
    }

    const ndjson = toNdjson(bundle)
    expect(ndjson.split('\n')).toHaveLength(2)
    expect(parseNdjson(ndjson)).toEqual(bundle)
  })

  it('throws on an empty bundle', () => {
    expect(() => parseNdjson('')).toThrow()
  })
})
