import { describe, expect, it } from 'vitest'
import { WebBluetoothInputSource } from './WebBluetoothInputSource'

// A hand-built mock of the exact `Bluetooth`/GATT surface this adapter
// calls -- Web Bluetooth has ZERO support in Firefox or Safari (ADR-070's
// own verified finding: Mozilla has explicitly declined it, WebKit ships
// none of its code) and no headless equivalent to run against for real.
function makeMockBluetooth() {
  const listeners: Array<(event: { target: { value: DataView | null } }) => void> = []
  const characteristic = {
    startNotifications: async () => {},
    stopNotifications: async () => {},
    addEventListener: (_t: 'characteristicvaluechanged', l: (event: { target: { value: DataView | null } }) => void) => listeners.push(l),
    removeEventListener: (_t: 'characteristicvaluechanged', l: (event: { target: { value: DataView | null } }) => void) => {
      const i = listeners.indexOf(l)
      if (i >= 0) listeners.splice(i, 1)
    },
  }
  const service = { getCharacteristic: async () => characteristic }
  const server = { connect: async () => server, disconnect: () => {}, getPrimaryService: async () => service }
  const device = { gatt: server }
  const bluetooth = { requestDevice: async () => device }
  return { bluetooth, fireNotification: (value: DataView | null) => listeners.forEach((l) => l({ target: { value } })) }
}

describe('WebBluetoothInputSource (ADR-070)', () => {
  it('reports unavailable when no Bluetooth implementation is supplied (Firefox/Safari)', () => {
    const source = new WebBluetoothInputSource('service-uuid', 'char-uuid', () => null, undefined)
    expect(source.isAvailable()).toBe(false)
  })

  it('connects GATT, subscribes to notifications via the native picker, and delivers a parsed reading', async () => {
    const { bluetooth, fireNotification } = makeMockBluetooth()
    const source = new WebBluetoothInputSource('service-uuid', 'char-uuid', (data) => data.getUint8(0), bluetooth as never)

    const readings: unknown[] = []
    source.onReading((r) => readings.push(r))
    await source.requestDevice()

    fireNotification(new DataView(new Uint8Array([72]).buffer)) // e.g. a heart-rate BLE reading

    expect(readings).toHaveLength(1)
    expect(readings[0]).toMatchObject({ value: 72 })

    await source.disconnect()
  })

  it('ignores a notification event carrying no value', async () => {
    const { bluetooth, fireNotification } = makeMockBluetooth()
    const source = new WebBluetoothInputSource('service-uuid', 'char-uuid', (data) => data.getUint8(0), bluetooth as never)

    const readings: unknown[] = []
    source.onReading((r) => readings.push(r))
    await source.requestDevice()
    fireNotification(null)

    expect(readings).toHaveLength(0)
  })
})
