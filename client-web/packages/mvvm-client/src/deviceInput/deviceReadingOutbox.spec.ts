import { describe, expect, it } from 'vitest'
import { toOutboxEntry } from './deviceReadingOutbox'
import type { DeviceReading } from './types'

describe('toOutboxEntry (ADR-070 -- per-integration schema choice)', () => {
  const reading: DeviceReading = { timestamp: '2026-08-11T10:00:00.000Z', value: { bpm: 72 }, monotonicElapsedMicros: 1234 }

  it('shapes a discrete mapping as an ordinary publish-bound outbox entry, defaulting to reviewPending (ADR-070\'s non-authoritative default)', () => {
    const entry = toOutboxEntry('instance-a', reading, { kind: 'discrete', appId: 'clinical-1', eventType: 'InstrumentReading', entityId: 'clinical-1:instrumentreading:r-1' })

    expect(entry.deliveryKind).toBeUndefined()
    expect(entry.channelId).toBeUndefined()
    expect(entry.appId).toBe('clinical-1')
    expect(entry.eventType).toBe('InstrumentReading')
    expect(entry.entityId).toBe('clinical-1:instrumentreading:r-1')
    expect(JSON.parse(entry.patch)).toEqual({ bpm: 72 })
    expect(entry.status).toBe('Pending')
    expect(entry.reviewPending).toBe(true)
    expect(entry.attestedActorId).toBeUndefined()
  })

  it('carries a device\'s own self-attested identity through instead of the reviewPending default', () => {
    const entry = toOutboxEntry('instance-a', reading, {
      kind: 'discrete', appId: 'clinical-1', eventType: 'InstrumentReading', entityId: 'clinical-1:instrumentreading:r-1',
      deviceAttestation: { actorId: 'did:key:z6Mk...device1', claims: { ucan: 'eyJ...' } },
    })

    expect(entry.reviewPending).toBeUndefined()
    expect(entry.attestedActorId).toBe('did:key:z6Mk...device1')
    expect(entry.attestedClaims).toEqual({ ucan: 'eyJ...' })
  })

  it('shapes a continuous mapping as a streamingSample-bound outbox entry, with no appId/eventType/entityId', () => {
    const entry = toOutboxEntry('instance-a', reading, { kind: 'continuous', channelId: 'vitals-waveform-1' })

    expect(entry.deliveryKind).toBe('streamingSample')
    expect(entry.channelId).toBe('vitals-waveform-1')
    expect(entry.monotonicElapsedMicros).toBe(1234)
    expect(entry.appId).toBe('')
    expect(entry.eventType).toBe('')
    expect(JSON.parse(entry.patch)).toEqual({ bpm: 72 })
  })

  it('gives every entry a fresh, unique commandId', () => {
    const a = toOutboxEntry('instance-a', reading, { kind: 'continuous', channelId: 'c-1' })
    const b = toOutboxEntry('instance-a', reading, { kind: 'continuous', channelId: 'c-1' })
    expect(a.commandId).not.toBe(b.commandId)
  })
})
