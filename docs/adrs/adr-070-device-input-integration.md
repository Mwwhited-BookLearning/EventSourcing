[← ADR index](../07-adrs.md)

# ADR-070: Device input integration — WebUSB/WebHID/Web Serial/Web Bluetooth, with a native-bridge fallback

Status: Accepted

Context: How do HID, raw USB, serial, or other device streams (BLE
included) get into the web-based MVVM client (`ADR-039`), particularly
in offline mode? Researched the real browser platform capabilities
before designing anything bespoke, per this project's standing
convention, rather than assuming a mechanism was needed:

- **WebUSB** (raw USB, byte-level), **Web Serial** (RS-232/serial-port
  devices), **WebHID** (HID-class devices — common for medical and
  custom peripherals), and **Web Bluetooth** (BLE) all give a web page
  direct hardware access with **no network involved at all** — exactly
  the property needed for offline capture.
- **Checked browser support rather than assumed, and it's a real
  constraint, not a footnote**: as of this writing, WebUSB (~76%
  global), Web Serial (~72%), and WebHID (~27%, the weakest of the
  four) all ship in Chromium-based desktop browsers only. **Web
  Bluetooth, WebHID, and Web Serial have zero support in Firefox and
  zero in Safari (macOS or iOS)** — Mozilla has explicitly declined Web
  Bluetooth on its own standards-positions list; WebKit ships no Web
  Bluetooth code at all. The device-capture half of this client is
  Chromium-only unless a fallback exists.
- **The real fallback, already used in the wild for exactly this gap**:
  a local native "bridge" companion application with genuine OS-level
  hardware access, exposing a local WebSocket/HTTP server (`localhost`
  or a local network address) the web app talks to instead of the
  hardware directly — the same shape real BLE-health-device bridges
  (e.g., "Haven Connect" for iOS/Safari) already use to close this exact
  gap.
- All four browser APIs require a secure context (HTTPS, or `localhost`
  for dev) and an explicit user gesture triggering a native device-
  picker dialog — no silent/automatic hardware access, by the platform's
  own design, not something this framework layers on top.

Decision:
- **A new extensibility seam, `IDeviceInputSource`** (added to
  `docs/extensibility-points.md`, the same keyed-DI/Strategy-pattern
  shape every other seam in this design already uses) — one small
  adapter per hardware-interface type: `WebUsbInputSource`,
  `WebHidInputSource`, `WebSerialInputSource`, `WebBluetoothInputSource`,
  plus `NativeBridgeInputSource` (talks to a local companion app over a
  `localhost` WebSocket) for Firefox/Safari or any device whose
  interface none of the four browser APIs reach. **Which adapter applies
  isn't a deployment-wide pick — it's dictated by the physical device's
  own interface**: a serial instrument uses `WebSerialInputSource`, a
  BLE monitor uses `WebBluetoothInputSource`, and so on; multiple
  adapters are routinely active at once in the same client, the same
  multi-simultaneous-implementation shape `ADR-057`'s `IErasureKeyStore`
  already established for a different seam.
- **Captured readings feed `ADR-039`'s existing durable outbox
  unchanged** — no new local-storage mechanism. These APIs only work
  from an open page/window context (not inside a Service Worker), so the
  open app window reads from the device and writes into the outbox; the
  Service Worker's job stays exactly what `ADR-069` already made it —
  flushing that outbox whenever a trigger (opportunistic/scheduled/
  manual) fires, with no awareness of where a queued item originally
  came from.
- **A device's physical location doesn't constrain where the resulting
  data may replicate** — `ADR-061`'s region-pinning is stated explicitly
  as a client is a leaf consumer of one server site, not a fourth
  peer/site itself; a device-captured reading, once published, inherits
  whatever `AllowedRegions` its `AppId` carries at the server, same as
  any other event this framework accepts, regardless of which region
  the capturing device happened to be sitting in.
- **Server-side mapping is a per-integration schema choice, not a
  framework-wide rule** — continuous, high-frequency device output
  (a vitals waveform) maps to `ADR-031`'s Streaming Channels; a discrete
  reading (a one-shot instrument result) maps to an ordinary published
  event. **Defaults to `ADR-035`'s non-authoritative capture** — a raw
  device reading captured client-side, possibly while offline for a
  long stretch (`ADR-069`), hasn't been reviewed and shouldn't be treated
  as automatically trustworthy — unless the specific device itself
  carries a self-attested identity (`ADR-036`'s DID/UCAN), in which case
  that attestation travels with the reading the same way any other
  self-attested submission already does.
  **Clarified, 2026-08-11 (build-plan item 44), not a reversal**: "non-
  authoritative" above is this ADR's own descriptive phrase, not a
  literal `AuthorityStatus` value — `PublishService`'s three real values
  (`ADR-042`) are `"accepted"`, `"unattested"`, and `"pending_review"`,
  and the literal absence of every attestation field actually produces
  `"accepted"`, not a non-authoritative one. The default this bullet
  describes is realized via `ReviewPending` (`ADR-042`'s content/
  confidence trigger — an honest fit for "a raw, un-reviewed reading
  with no identity claim attached"), not via `AttestedActorId`/
  `AttestedClaims` (`ADR-042`'s IDENTITY-claim trigger, used instead
  only when a device presents a REAL self-attested identity) — see
  `client-web/src/deviceInput/deviceReadingOutbox.ts`.

Consequences:
- `docs/extensibility-points.md` gains the `IDeviceInputSource` row.
- `docs/patterns/README.md` gains a "Device input integration via Web
  Hardware APIs" entry in the decided-not-yet-written-up table.
- The Chromium-only reality of three of the four APIs is a real
  constraint on which browsers can do *direct* device capture — stated
  plainly rather than glossed over; the native-bridge fallback is what
  makes Firefox/Safari support possible at all, at the cost of a second
  companion application to build and maintain for those platforms.
- No new consent/security mechanism — secure-context and user-gesture
  requirements are the browser platform's own, not reimplemented here.
