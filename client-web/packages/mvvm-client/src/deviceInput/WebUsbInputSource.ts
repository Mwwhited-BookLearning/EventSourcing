import type { DeviceReading, IDeviceInputSource, ParseFn } from './types'
import { RecordingAgent } from './RecordingAgent'

// Minimal ambient shape for WebUSB -- Chromium-only, ~76% global support,
// the strongest of the four (ADR-070's own verified finding); not in
// TypeScript's default DOM lib.
interface UsbTransferInResult {
  data?: DataView
}
interface UsbDevice {
  open(): Promise<void>
  close(): Promise<void>
  selectConfiguration(configurationValue: number): Promise<void>
  claimInterface(interfaceNumber: number): Promise<void>
  transferIn(endpointNumber: number, length: number): Promise<UsbTransferInResult>
}
interface Usb {
  requestDevice(options: { filters: unknown[] }): Promise<UsbDevice>
}

export class WebUsbInputSource implements IDeviceInputSource {
  readonly kind = 'WebUsb'
  private device: UsbDevice | null = null
  private connected = false
  private callback: ((reading: DeviceReading) => void) | null = null
  private readonly agent = new RecordingAgent()

  constructor(
    private readonly parse: ParseFn,
    private readonly usb: Usb | undefined = (navigator as unknown as { usb?: Usb }).usb,
    private readonly endpointNumber = 1,
    private readonly transferLength = 64,
  ) {}

  isAvailable(): boolean {
    return this.usb !== undefined
  }

  async requestDevice(): Promise<void> {
    if (!this.usb) throw new Error('WebUSB API is not available in this browser')
    this.device = await this.usb.requestDevice({ filters: [] }) // the native device-picker dialog (ADR-070) -- a real user gesture, never bypassed
    await this.device.open()
    await this.device.selectConfiguration(1)
    await this.device.claimInterface(0)
    this.connected = true
    void this.readLoop()
  }

  onReading(callback: (reading: DeviceReading) => void): void {
    this.callback = callback
  }

  async disconnect(): Promise<void> {
    this.connected = false
    await this.device?.close()
    this.device = null
  }

  private async readLoop(): Promise<void> {
    while (this.connected && this.device) {
      const result = await this.device.transferIn(this.endpointNumber, this.transferLength)
      if (result.data) {
        const reading: DeviceReading = {
          timestamp: new Date().toISOString(),
          value: this.parse(result.data),
          monotonicElapsedMicros: this.agent.elapsedMicros(),
        }
        this.callback?.(reading)
      }
    }
  }
}
