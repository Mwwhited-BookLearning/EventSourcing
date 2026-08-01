[← ADR index](../07-adrs.md)

# ADR-083: Optional monotonic elapsed-time capture, alongside wall-clock `Timestamp`, for device-telemetry clock-lie detection

Status: Accepted

Context: `docs/10-open-questions.md`'s clock-authority question was
already mostly resolved — `SequenceNumber`+`ChainHash` (`ADR-019`)
guarantee ordering/tamper-evidence independent of `OccurredAt`'s
truthfulness, so no new "whose clock is authoritative" policy is needed.
One narrower residual remained: should a device optionally also capture
a *second*, relative/monotonic timer alongside wall-clock `Timestamp`,
so a genuinely lying wall-clock becomes *detectable* (not just
irrelevant to integrity), for `ADR-070`-sourced device telemetry
specifically? Direct design conversation resolved this session: **yes**,
captured by the client-side **recording agent** — whatever software
component owns a recording session's lifecycle (the same "session"
concept `ADR-081`'s `ThreadId` groups channels under).

Decision:
- **`TelemetrySample` gains an optional `MonotonicElapsedMicros`**
  (`docs/data/streaming-and-attachments.md`) — elapsed time since the
  recording agent's own session start, read from the device/client's
  monotonic clock source (immune to wall-clock adjustment, NTP
  correction, or deliberate tampering), captured alongside the existing
  wall-clock `Timestamp` (`ADR-029`'s discipline, unchanged). Optional
  because not every producer has a meaningful monotonic session concept
  (a fixed-rate sensor with no client-side "recording agent" software at
  all has nothing to report here) — this is an enhancement for the
  specific case that motivated it, not a new requirement on every
  channel.
- **The recording agent is the capturing authority, not the device's raw
  sensor and not the server.** The same client-side component that would
  own `ADR-081`'s session grouping (assigning a `ThreadId`) is the
  natural owner of "session start" for the monotonic clock too — one
  session concept, not two independently-defined ones.
- **Detection is a downstream, application-level analysis, not a new
  framework mechanism.** The framework's job is capturing both values
  side by side; comparing claimed wall-clock deltas against actual
  monotonic deltas across a sample sequence to flag a suspiciously
  inconsistent wall clock is ordinary analysis over already-captured
  data (the same "framework provides the primitive, detection stays an
  application concern" posture `ADR-031` already takes for anomaly
  detection generally) — no new detector interface, no new envelope
  field on `StoredEvent`.
- **RFC 3161 timestamping (the open question's other, distinct residual)
  remains fully open, unaffected by this decision** — a separate
  question about `ADR-066` signatures and `ADR-068` litigation exports,
  not resolved here.

Consequences:
- `docs/data/streaming-and-attachments.md`'s `TelemetrySample` gains the
  new field in this same pass, per this project's data-model-ownership
  convention.
- `ADR-070`'s device-input integration doc should eventually note that a
  device bridge capable of exposing a monotonic clock source (most
  platform device APIs already provide one) should populate this field
  when registering a continuously-monitored channel — flagged as
  propagation work, not required for this ADR itself.
- Partially resolves `docs/10-open-questions.md` row 11 — the RFC 3161
  half stays open.
