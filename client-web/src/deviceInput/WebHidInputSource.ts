import type { DeviceReading, IDeviceInputSource, ParseFn } from './types'
import { RecordingAgent } from './RecordingAgent'

// Minimal ambient shape for WebHID -- Chromium-only, ~27% global support,
// the weakest of the four browser APIs (ADR-070's own verified finding);
// not in TypeScript's default DOM lib.
interface HidInputReportEvent {
  data: DataView
}
interface HidDevice {
  open(): Promise<void>
  close(): Promise<void>
  addEventListener(type: 'inputreport', listener: (event: HidInputReportEvent) => void): void
  removeEventListener(type: 'inputreport', listener: (event: HidInputReportEvent) => void): void
}
interface Hid {
  requestDevice(options: { filters: unknown[] }): Promise<HidDevice[]>
}

export class WebHidInputSource implements IDeviceInputSource {
  readonly kind = 'WebHid'
  private device: HidDevice | null = null
  private callback: ((reading: DeviceReading) => void) | null = null
  private readonly agent = new RecordingAgent()
  private readonly listener = (event: HidInputReportEvent) => {
    const reading: DeviceReading = {
      timestamp: new Date().toISOString(),
      value: this.parse(event.data),
      monotonicElapsedMicros: this.agent.elapsedMicros(),
    }
    this.callback?.(reading)
  }

  constructor(
    private readonly parse: ParseFn,
    private readonly hid: Hid | undefined = (navigator as unknown as { hid?: Hid }).hid,
  ) {}

  isAvailable(): boolean {
    return this.hid !== undefined
  }

  async requestDevice(): Promise<void> {
    if (!this.hid) throw new Error('WebHID API is not available in this browser')
    const devices = await this.hid.requestDevice({ filters: [] }) // the native device-picker dialog (ADR-070) -- a real user gesture, never bypassed
    const device = devices[0]
    if (!device) return
    this.device = device
    await device.open()
    device.addEventListener('inputreport', this.listener)
  }

  onReading(callback: (reading: DeviceReading) => void): void {
    this.callback = callback
  }

  async disconnect(): Promise<void> {
    this.device?.removeEventListener('inputreport', this.listener)
    await this.device?.close()
    this.device = null
  }
}
