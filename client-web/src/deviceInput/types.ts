// ADR-070's IDeviceInputSource seam -- one small adapter per hardware
// interface. Byte-level parsing is deliberately NOT generic: what a raw
// USB/HID/serial/BLE frame actually means is the INTEGRATION's own
// business, not this framework's (the same reasoning `IEventUpcaster`
// leaves reshape logic to the registering integration rather than
// guessing a universal transform) -- every adapter below takes a `parse`
// function injected by the caller instead of hardcoding one.
export interface DeviceReading {
  timestamp: string // wall-clock ISO 8601 (ADR-029's existing discipline)
  value: unknown
  monotonicElapsedMicros?: number // ADR-083, from RecordingAgent
}

export type ParseFn = (data: DataView) => unknown

export interface IDeviceInputSource {
  readonly kind: string
  isAvailable(): boolean
  // Triggers the browser's own native device-picker dialog (a real user
  // gesture is required by the platform itself, ADR-070 -- never
  // simulated or bypassed here).
  requestDevice(): Promise<void>
  onReading(callback: (reading: DeviceReading) => void): void
  disconnect(): Promise<void>
}
