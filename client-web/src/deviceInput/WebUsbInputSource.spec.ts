import { describe, expect, it } from 'vitest'
import { WebUsbInputSource } from './WebUsbInputSource'

// A hand-built mock of the exact `Usb`/`UsbDevice` surface this adapter
// calls -- WebUSB (the strongest-supported of the four, ~76% global,
// ADR-070's own verified figure, still Chromium-desktop-only) has no
// headless equivalent to run against for real.
function makeMockDevice(readings: Uint8Array[]) {
  const queue = [...readings]
  const device = {
    open: async () => {},
    close: async () => {},
    selectConfiguration: async () => {},
    claimInterface: async () => {},
    transferIn: async () => {
      const next = queue.shift()
      return next ? { data: new DataView(next.buffer) } : { data: undefined }
    },
  }
  return device
}

describe('WebUsbInputSource (ADR-070)', () => {
  it('reports unavailable when no USB implementation is supplied', () => {
    const source = new WebUsbInputSource(() => null, undefined)
    expect(source.isAvailable()).toBe(false)
  })

  it('opens, configures, and claims the interface via the native picker, then delivers a parsed reading', async () => {
    const device = makeMockDevice([new Uint8Array([9])])
    const usb = { requestDevice: async () => device }
    const source = new WebUsbInputSource((data) => data.getUint8(0), usb as never)

    const readingPromise = new Promise((resolve) => source.onReading(resolve))
    await source.requestDevice()
    const reading = await readingPromise
    await source.disconnect() // stops the read loop before the test ends

    expect(reading).toMatchObject({ value: 9 })
  })
})
