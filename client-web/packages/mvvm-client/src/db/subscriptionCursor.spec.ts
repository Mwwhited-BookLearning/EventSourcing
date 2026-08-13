import { beforeEach, describe, expect, it } from 'vitest'
import { resetDbConnectionForTests } from './indexedDb'
import { getCursor, setCursor } from './subscriptionCursor'

describe('subscriptionCursor (TODO.md\'s resume-cursor gap)', () => {
  beforeEach(() => {
    resetDbConnectionForTests()
  })

  it('defaults to 0 for a subscription target with no persisted cursor yet', async () => {
    expect(await getCursor('instance-a', 'mvvm-demo', 'OrderPlaced')).toBe(0)
  })

  it('persists and round-trips a cursor value', async () => {
    await setCursor('instance-a', 'mvvm-demo', 'OrderPlaced', 42)
    expect(await getCursor('instance-a', 'mvvm-demo', 'OrderPlaced')).toBe(42)
  })

  it('overwrites a previously persisted cursor, never accumulates', async () => {
    await setCursor('instance-a', 'mvvm-demo', 'OrderPlaced', 5)
    await setCursor('instance-a', 'mvvm-demo', 'OrderPlaced', 12)
    expect(await getCursor('instance-a', 'mvvm-demo', 'OrderPlaced')).toBe(12)
  })

  it('keeps distinct (instanceId, appId, eventType) targets independent', async () => {
    await setCursor('instance-a', 'mvvm-demo', 'OrderPlaced', 5)
    await setCursor('instance-a', 'mvvm-demo', 'EntityErasureRequested', 9)
    await setCursor('instance-b', 'mvvm-demo', 'OrderPlaced', 20)

    expect(await getCursor('instance-a', 'mvvm-demo', 'OrderPlaced')).toBe(5)
    expect(await getCursor('instance-a', 'mvvm-demo', 'EntityErasureRequested')).toBe(9)
    expect(await getCursor('instance-b', 'mvvm-demo', 'OrderPlaced')).toBe(20)
  })
})
