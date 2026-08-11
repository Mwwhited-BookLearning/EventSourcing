import type { DeviceReading, IDeviceInputSource, ParseFn } from './types'
import { RecordingAgent } from './RecordingAgent'

export class DeviceCaptureUnavailableError extends Error {
  constructor() {
    // ADR-070's own exit criterion: "when the native companion app isn't
    // running, the client reports capture unavailable and prompts the
    // user to launch it" -- this framework never attempts to auto-launch
    // a native process itself (out of browser-sandbox scope, stated
    // explicitly in the ADR's own sequence diagram note).
    super('Device capture is unavailable -- the native companion app does not appear to be running. Please launch it and try again.')
  }
}

// Firefox/Safari (zero Web Bluetooth/WebHID/Web Serial support) or any
// device whose interface none of the four browser APIs reach -- talks to
// a real, separate native companion application over a localhost
// WebSocket, per ADR-070's own researched fallback shape (the same one
// real BLE-health-device bridges already use). The companion app itself
// is real, separate software this framework does not build for the
// deployer (ADR-070's own Decision text) -- see `client-web/native-
// bridge-reference/` for a minimal, genuinely-runnable reference
// implementation of the wire protocol this adapter speaks, used to prove
// this class against a real WebSocket server, not just a mock.
export class NativeBridgeInputSource implements IDeviceInputSource {
  readonly kind = 'NativeBridge'
  private socket: WebSocket | null = null
  private callback: ((reading: DeviceReading) => void) | null = null
  private readonly agent = new RecordingAgent()

  constructor(
    private readonly bridgeUrl: string,
    private readonly deviceId: string,
    private readonly parse: ParseFn,
    private readonly WebSocketImpl: typeof WebSocket = WebSocket,
  ) {}

  isAvailable(): boolean {
    return typeof this.WebSocketImpl !== 'undefined'
  }

  async requestDevice(): Promise<void> {
    return new Promise((resolve, reject) => {
      const socket = new this.WebSocketImpl(this.bridgeUrl)
      this.socket = socket

      socket.addEventListener('open', () => {
        socket.send(JSON.stringify({ type: 'connect', deviceId: this.deviceId }))
        resolve()
      })
      socket.addEventListener('error', () => reject(new DeviceCaptureUnavailableError()))
      socket.addEventListener('close', () => reject(new DeviceCaptureUnavailableError()))
      socket.addEventListener('message', (event: MessageEvent<string>) => {
        const message = JSON.parse(event.data) as { type: string; data?: string }
        if (message.type !== 'reading' || !message.data) return

        const bytes = Uint8Array.from(atob(message.data), (c) => c.charCodeAt(0))
        const reading: DeviceReading = {
          timestamp: new Date().toISOString(),
          value: this.parse(new DataView(bytes.buffer)),
          monotonicElapsedMicros: this.agent.elapsedMicros(),
        }
        this.callback?.(reading)
      })
    })
  }

  onReading(callback: (reading: DeviceReading) => void): void {
    this.callback = callback
  }

  async disconnect(): Promise<void> {
    this.socket?.close()
    this.socket = null
  }
}
