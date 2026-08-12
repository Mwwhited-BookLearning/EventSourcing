import { describe, expect, it } from 'vitest'
import { RecordingAgent } from './RecordingAgent'

describe('RecordingAgent (ADR-083)', () => {
  it('reports elapsed microseconds since its own construction, not since some other origin', async () => {
    const agent = new RecordingAgent()
    await new Promise((resolve) => setTimeout(resolve, 20))
    const elapsed = agent.elapsedMicros()
    expect(elapsed).toBeGreaterThan(10_000) // at least ~10ms in microseconds
  })

  it('reports monotonically non-decreasing values across successive calls', async () => {
    const agent = new RecordingAgent()
    const first = agent.elapsedMicros()
    await new Promise((resolve) => setTimeout(resolve, 5))
    const second = agent.elapsedMicros()
    expect(second).toBeGreaterThanOrEqual(first)
  })
})
