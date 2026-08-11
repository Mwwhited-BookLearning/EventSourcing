import type { DeviceReading, IDeviceInputSource, ParseFn } from './types'
import { RecordingAgent } from './RecordingAgent'

// Minimal ambient shape for Web Bluetooth -- ZERO support in Firefox or
// Safari (Mozilla has explicitly declined it; WebKit ships none of its
// code, ADR-070's own verified finding) -- not in TypeScript's default
// DOM lib.
interface CharacteristicValueChangedEvent {
  target: { value: DataView | null }
}
interface BluetoothCharacteristic {
  startNotifications(): Promise<void>
  stopNotifications(): Promise<void>
  addEventListener(type: 'characteristicvaluechanged', listener: (event: CharacteristicValueChangedEvent) => void): void
  removeEventListener(type: 'characteristicvaluechanged', listener: (event: CharacteristicValueChangedEvent) => void): void
}
interface BluetoothService {
  getCharacteristic(characteristicUuid: string): Promise<BluetoothCharacteristic>
}
interface BluetoothRemoteGattServer {
  connect(): Promise<BluetoothRemoteGattServer>
  disconnect(): void
  getPrimaryService(serviceUuid: string): Promise<BluetoothService>
}
interface BluetoothDevice {
  gatt?: BluetoothRemoteGattServer
}
interface Bluetooth {
  requestDevice(options: { acceptAllDevices?: boolean; filters?: unknown[] }): Promise<BluetoothDevice>
}

export class WebBluetoothInputSource implements IDeviceInputSource {
  readonly kind = 'WebBluetooth'
  private characteristic: BluetoothCharacteristic | null = null
  private device: BluetoothDevice | null = null
  private callback: ((reading: DeviceReading) => void) | null = null
  private readonly agent = new RecordingAgent()
  private readonly listener = (event: CharacteristicValueChangedEvent) => {
    if (!event.target.value) return
    const reading: DeviceReading = {
      timestamp: new Date().toISOString(),
      value: this.parse(event.target.value),
      monotonicElapsedMicros: this.agent.elapsedMicros(),
    }
    this.callback?.(reading)
  }

  constructor(
    private readonly serviceUuid: string,
    private readonly characteristicUuid: string,
    private readonly parse: ParseFn,
    private readonly bluetooth: Bluetooth | undefined = (navigator as unknown as { bluetooth?: Bluetooth }).bluetooth,
  ) {}

  isAvailable(): boolean {
    return this.bluetooth !== undefined
  }

  async requestDevice(): Promise<void> {
    if (!this.bluetooth) throw new Error('Web Bluetooth API is not available in this browser')
    this.device = await this.bluetooth.requestDevice({ acceptAllDevices: true }) // the native device-picker dialog (ADR-070) -- a real user gesture, never bypassed
    const server = await this.device.gatt?.connect()
    const service = await server?.getPrimaryService(this.serviceUuid)
    this.characteristic = (await service?.getCharacteristic(this.characteristicUuid)) ?? null
    await this.characteristic?.startNotifications()
    this.characteristic?.addEventListener('characteristicvaluechanged', this.listener)
  }

  onReading(callback: (reading: DeviceReading) => void): void {
    this.callback = callback
  }

  async disconnect(): Promise<void> {
    this.characteristic?.removeEventListener('characteristicvaluechanged', this.listener)
    await this.characteristic?.stopNotifications()
    this.device?.gatt?.disconnect()
    this.device = null
    this.characteristic = null
  }
}
