import { describe, expect, it } from 'vitest'
import { WebSerialInputSource } from './WebSerialInputSource'

// Web Serial has no real implementation available outside an actual
// Chromium browser with a physical device attached -- there is no
// headless/Node equivalent to run this against for real, unlike
// NativeBridgeInputSource's genuine WebSocket round trip. A hand-built
// mock of the exact `Serial`/`SerialPort` surface this adapter calls is
// the correct test double here: it proves THIS adapter's own logic
// (feature detection, the open/read loop, parsing, disconnect) against
// the real documented API shape, which is everything under this
// framework's own control to verify without a browser.
function makeMockSerial(chunks: Uint8Array[]) {
  let resolveNext: ((value: { done: boolean; value?: Uint8Array }) => void) | null = null
  const queue = [...chunks]

  const reader = {
    read: () =>
      new Promise<{ done: boolean; value?: Uint8Array }>((resolve) => {
        if (queue.length > 0) resolve({ done: false, value: queue.shift() })
        else resolveNext = resolve // last read() call hangs -- disconnect() below resolves it with done:true
      }),
    cancel: async () => {
      resolveNext?.({ done: true })
    },
  }

  const port = {
    open: async () => {},
    close: async () => {},
    readable: { getReader: () => reader },
  }

  const serial = { requestPort: async () => port }
  return { serial, port }
}

describe('WebSerialInputSource (ADR-070)', () => {
  it('reports unavailable when no Serial implementation is supplied (Firefox/Safari)', () => {
    const source = new WebSerialInputSource(() => null, undefined)
    expect(source.isAvailable()).toBe(false)
  })

  it('opens the port via the real device-picker call and delivers parsed readings with a monotonic timestamp', async () => {
    const { serial } = makeMockSerial([new Uint8Array([1, 2, 3, 4])])
    const source = new WebSerialInputSource((data) => data.getUint32(0), serial as never)

    const readingPromise = new Promise((resolve) => source.onReading(resolve))
    await source.requestDevice()
    const reading = await readingPromise

    expect(reading).toMatchObject({ value: 0x01020304 })
    expect((reading as { monotonicElapsedMicros: number }).monotonicElapsedMicros).toBeGreaterThanOrEqual(0)
    await source.disconnect()
  })

  it('throws rather than silently no-op-ing when requestDevice is called with no Serial support', async () => {
    const source = new WebSerialInputSource(() => null, undefined)
    await expect(source.requestDevice()).rejects.toThrow(/Web Serial/)
  })
})
