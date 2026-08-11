import { describe, expect, it } from 'vitest'
import { exportOutboxBundle, importOutboxBundle } from './exportImport'
import type { ClientOutboxEntry } from '../types'

function makeEntry(overrides: Partial<ClientOutboxEntry> = {}): ClientOutboxEntry {
  return {
    commandId: 'cmd-1', instanceId: 'instance-a', appId: 'mvvm-demo', eventType: 'OrderPlaced', entityId: 'mvvm-demo:orderplaced:o-1',
    expectedVersion: null, schemaVersion: 1, patch: JSON.stringify({ Amount: 175.0 }), status: 'Pending', enqueuedAt: new Date().toISOString(),
    attempts: 0, ...overrides,
  }
}

describe('exportOutboxBundle/importOutboxBundle (ADR-069 air-gapped transfer)', () => {
  it('exports only Pending entries, and the exported bundle verifies on import', async () => {
    const entries = [makeEntry(), makeEntry({ commandId: 'cmd-2', status: 'Delivered' })]
    const ndjson = await exportOutboxBundle(entries, 'instance-a')

    const result = await importOutboxBundle(ndjson)
    expect(result.verified).toBe(true)
    expect(result.entries).toHaveLength(1)
    expect(result.entries[0]?.commandId).toBe('cmd-1')
    expect(result.entries[0]).not.toHaveProperty('contentHash')
  })

  it('rejects a tampered bundle before returning any entry', async () => {
    const ndjson = await exportOutboxBundle([makeEntry()], 'instance-a')
    const tampered = ndjson.replace('"manifestHash":"', '"manifestHash":"tampered')

    const result = await importOutboxBundle(tampered)
    expect(result.verified).toBe(false)
    expect(result.entries).toHaveLength(0)
  })

  it('rejects a bundle whose entry content was altered after export, even though the manifest hash (over the untouched contentHash list) still matches', async () => {
    const ndjson = await exportOutboxBundle([makeEntry()], 'instance-a')
    const tampered = ndjson.replace('\\"Amount\\":175', '\\"Amount\\":999999')

    const result = await importOutboxBundle(tampered)
    expect(result.verified).toBe(false)
    expect(result.entries).toHaveLength(0)
  })
})
