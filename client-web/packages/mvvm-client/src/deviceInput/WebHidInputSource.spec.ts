import { describe, expect, it } from 'vitest'
import { WebHidInputSource } from './WebHidInputSource'

// A hand-built mock of the exact `Hid`/`HidDevice` surface this adapter
// calls -- WebHID (the weakest-supported of the four browser APIs,
// ADR-070's own verified ~27% figure) has no headless equivalent to run
// against for real, the same reasoning WebSerialInputSource's own spec
// states.
function makeMockDevice() {
  const listeners: Array<(event: { data: DataView }) => void> = []
  const device = {
    open: async () => {},
    close: async () => {},
    addEventListener: (_type: 'inputreport', listener: (event: { data: DataView }) => void) => listeners.push(listener),
    removeEventListener: (_type: 'inputreport', listener: (event: { data: DataView }) => void) => {
      const i = listeners.indexOf(listener)
      if (i >= 0) listeners.splice(i, 1)
    },
  }
  return { device, fireReport: (data: DataView) => listeners.forEach((l) => l({ data })) }
}

describe('WebHidInputSource (ADR-070)', () => {
  it('reports unavailable when no HID implementation is supplied', () => {
    const source = new WebHidInputSource(() => null, undefined)
    expect(source.isAvailable()).toBe(false)
  })

  it('requests a device via the native picker, then delivers a reading for every inputreport event', async () => {
    const { device, fireReport } = makeMockDevice()
    const hid = { requestDevice: async () => [device] }
    const source = new WebHidInputSource((data) => data.getUint8(0), hid as never)

    const readings: unknown[] = []
    source.onReading((r) => readings.push(r))
    await source.requestDevice()

    fireReport(new DataView(new Uint8Array([42]).buffer))
    fireReport(new DataView(new Uint8Array([7]).buffer))

    expect(readings).toHaveLength(2)
    expect(readings[0]).toMatchObject({ value: 42 })
    expect(readings[1]).toMatchObject({ value: 7 })

    await source.disconnect()
  })

  it('does nothing if the user cancels the device picker (an empty device list)', async () => {
    const hid = { requestDevice: async () => [] }
    const source = new WebHidInputSource(() => null, hid as never)
    await expect(source.requestDevice()).resolves.toBeUndefined()
  })
})
