import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { WebSocketServer } from 'ws'
import { NativeBridgeInputSource, DeviceCaptureUnavailableError } from './NativeBridgeInputSource'

// A REAL WebSocket server (the `ws` package, real TCP sockets on
// localhost), not a mock -- proving NativeBridgeInputSource's actual wire
// protocol round-trips over a real socket, the same "always actually run
// it" discipline this repo applies to every other real network client
// (PeerSyncClient, FollowClient, WebhookOutboxPump all got real-HTTP
// tests, never mock-only). Node's own global `WebSocket` (stable since
// Node 22) is the real client this test exercises -- no mock handler
// substituted here, unlike the four browser-API adapters, which
// genuinely have no real implementation available outside an actual
// browser to test against.
describe('NativeBridgeInputSource (ADR-070, real WebSocket round trip)', () => {
  let server: WebSocketServer
  let port: number

  beforeEach(async () => {
    server = new WebSocketServer({ port: 0 })
    await new Promise<void>((resolve) => server.once('listening', resolve))
    port = (server.address() as { port: number }).port
  })

  afterEach(() => {
    server.close()
  })

  it('connects, sends the connect message, and delivers a reading forwarded by the bridge', async () => {
    server.on('connection', (socket) => {
      socket.on('message', (raw) => {
        const message = JSON.parse(raw.toString())
        expect(message).toEqual({ type: 'connect', deviceId: 'ble-hr-strap-01' })

        const bytes = new Uint8Array([0, 0, 0, 72]) // 72, big-endian uint32
        socket.send(JSON.stringify({ type: 'reading', data: Buffer.from(bytes).toString('base64') }))
      })
    })

    const source = new NativeBridgeInputSource(`ws://localhost:${port}`, 'ble-hr-strap-01', (data) => data.getUint32(0))

    const readingPromise = new Promise((resolve) => source.onReading(resolve))
    await source.requestDevice()
    const reading = await readingPromise

    expect(reading).toMatchObject({ value: 72 })
    expect((reading as { monotonicElapsedMicros: number }).monotonicElapsedMicros).toBeGreaterThanOrEqual(0)

    await source.disconnect()
  })

  it('reports capture as unavailable when nothing is listening (companion app not running)', async () => {
    await server.close() // release the port immediately -- nothing listens on it now
    const source = new NativeBridgeInputSource(`ws://localhost:${port}`, 'ble-hr-strap-01', (data) => data.getUint32(0))

    await expect(source.requestDevice()).rejects.toBeInstanceOf(DeviceCaptureUnavailableError)
  })

  it('isAvailable reflects whether a WebSocket implementation exists at all', () => {
    const source = new NativeBridgeInputSource(`ws://localhost:${port}`, 'device-1', (data) => data.getUint32(0))
    expect(source.isAvailable()).toBe(true)
  })
})
