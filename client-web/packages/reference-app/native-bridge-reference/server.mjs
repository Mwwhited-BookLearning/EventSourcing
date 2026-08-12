#!/usr/bin/env node
// ADR-070's native companion app, reference implementation. This is
// deliberately NOT the shipped, production companion app real
// deployers would run (that's genuinely separate software with real
// OS-level hardware access this framework does not build, per the
// ADR's own Decision text) -- it exists so NativeBridgeInputSource's
// wire protocol is provable against a real WebSocket server, not just a
// mock, the same "always actually run it" discipline this repo applies
// everywhere else. A real bridge would read from actual OS-level
// USB/serial/BLE APIs where this one simulates a fixed-interval sensor
// instead.
//
// Wire protocol (matches NativeBridgeInputSource.ts exactly):
//   client -> server: {"type":"connect","deviceId":"<id>"}
//   server -> client: {"type":"reading","data":"<base64 bytes>"}
//
// Usage: node server.mjs [port]
import { WebSocketServer } from 'ws'

const port = Number(process.argv[2] ?? 8787)
const wss = new WebSocketServer({ port })

wss.on('connection', (socket) => {
  let intervalHandle

  socket.on('message', (raw) => {
    const message = JSON.parse(raw.toString())
    if (message.type !== 'connect') return

    // Simulated sensor: one 4-byte big-endian reading per second. A real
    // bridge would forward genuine bytes read from the OS-level hardware
    // API instead of synthesizing them.
    let counter = 0
    intervalHandle = setInterval(() => {
      counter += 1
      const bytes = new Uint8Array(4)
      new DataView(bytes.buffer).setUint32(0, counter)
      const base64 = Buffer.from(bytes).toString('base64')
      socket.send(JSON.stringify({ type: 'reading', data: base64 }))
    }, 1000)
  })

  socket.on('close', () => clearInterval(intervalHandle))
})

console.log(`Reference native bridge listening on ws://localhost:${port}`)
