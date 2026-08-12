import type { DeviceReading, IDeviceInputSource, ParseFn } from './types'
import { RecordingAgent } from './RecordingAgent'

// Minimal ambient shape for the Web Serial API -- not in TypeScript's
// default DOM lib (Chromium-only, experimental, ADR-070's own verified
// finding), so declared locally rather than pulling in an unverified
// third-party @types package for a handful of methods.
interface SerialPort {
  open(options: { baudRate: number }): Promise<void>
  close(): Promise<void>
  readable: ReadableStream<Uint8Array> | null
}
interface Serial {
  requestPort(): Promise<SerialPort>
}

export class WebSerialInputSource implements IDeviceInputSource {
  readonly kind = 'WebSerial'
  private port: SerialPort | null = null
  private reader: ReadableStreamDefaultReader<Uint8Array> | null = null
  private callback: ((reading: DeviceReading) => void) | null = null
  private readonly agent = new RecordingAgent()

  constructor(
    private readonly parse: ParseFn,
    private readonly serial: Serial | undefined = (navigator as unknown as { serial?: Serial }).serial,
    private readonly baudRate = 9600,
  ) {}

  isAvailable(): boolean {
    return this.serial !== undefined
  }

  async requestDevice(): Promise<void> {
    if (!this.serial) throw new Error('Web Serial API is not available in this browser')
    this.port = await this.serial.requestPort() // the native device-picker dialog (ADR-070) -- a real user gesture, never bypassed
    await this.port.open({ baudRate: this.baudRate })
    void this.readLoop()
  }

  onReading(callback: (reading: DeviceReading) => void): void {
    this.callback = callback
  }

  async disconnect(): Promise<void> {
    await this.reader?.cancel()
    await this.port?.close()
    this.port = null
  }

  private async readLoop(): Promise<void> {
    if (!this.port?.readable) return
    this.reader = this.port.readable.getReader()
    while (true) {
      const { done, value } = await this.reader.read()
      if (done) break
      if (value) {
        const reading: DeviceReading = {
          timestamp: new Date().toISOString(),
          value: this.parse(new DataView(value.buffer, value.byteOffset, value.byteLength)),
          monotonicElapsedMicros: this.agent.elapsedMicros(),
        }
        this.callback?.(reading)
      }
    }
  }
}
