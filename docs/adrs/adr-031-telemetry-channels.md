[← ADR index](../07-adrs.md)

# ADR-031: Streaming channels (telemetry, audio/video) — a separate fast path, linked to events via `TelemetryPointer`

Status: Accepted

Context: This design's event log (`data/event-log.md`) is built for
discrete, schema-validated, semantically-rich business events — every
`StoredEvent` costs a JSON Schema check, a claim check, a hash-chain
computation, and an Entity Store fold. That's the right cost for an
`OrderPlaced` event; it's the wrong cost, by orders of magnitude, for a
raw signal sample — a voltage reading, a temperature, an EEG channel —
arriving at hundreds or thousands of samples per second. Forcing telemetry
through the same pipeline would make the pipeline's own correctness
machinery the bottleneck. There's a genuine second need underneath the
same roof: ingest raw signal data as fast as possible, read it back
(live-tailing or historical replay) as fast as possible, optionally
derive new streams from existing ones (resampling, filtering), and — the
actual bridge back to everything else this design already does — let a
detector that notices something in a stream publish an ordinary domain
event that points at exactly where in the stream it noticed it.

Decision:
- **A new, deliberately separate storage path: `TelemetryChannel` +
  `TelemetrySample`.** Not `StoredEvent`, not JSON-Schema-validated, not
  hash-chained per sample, not folded into the Entity Store. A channel
  declares a `ContentKind`:
  - `RawScalar` (`Float64`/`Int32`, one value per sample — a voltage, a
    temperature) or `RawBinary` (an opaque byte blob per sample), the
    original signal-telemetry case; or
  - `Media` (a sequence of codec-framed chunks — compressed or
    uncompressed audio/video), carrying a declared `MimeType` (e.g.
    `audio/opus`, `video/h264`) instead of a scalar type.

  Both shapes share every mechanic below (batch ingestion, tail/replay,
  derivation) — a "channel" is fundamentally *a sequenced stream of
  chunks with a declared content type*, and audio/video chunks are just
  a chunk kind this design didn't originally name, not a different
  mechanism. None of these get a JSON Schema, at any `ContentKind` —
  per-chunk structural validation is exactly the cost this whole path
  exists to avoid.
- **Channels belong to an entity** (`ChannelId`, `EntityId` — reusing
  `ADR-021`'s identity, so "this EEG channel belongs to `patient:123`"
  falls out for free) and to an application (`AppId`, `ADR-030`).
- **Ingestion is batch-first, not one-sample-per-request.** `POST
  /telemetry/{channelId}/samples` accepts a batch. For a fixed, known
  sample rate (the common case for a real sensor channel), the batch
  omits a per-sample timestamp entirely — `StartTimestamp` +
  `SampleIntervalMicros` + a flat values array — cutting the wire format
  roughly in half versus timestamping every sample individually. A
  channel with irregular/event-driven sampling can still send explicit
  `(timestamp, value)` pairs; both shapes are legal per channel, declared
  at channel registration. A `Media` channel's "batch" is one or more
  codec frames/segments with their own timestamps — the same batch
  ingestion path, just carrying opaque, already-encoded bytes per chunk
  instead of a scalar; this design has no opinion on codecs, containers,
  or keyframe placement, which stay entirely the producer's concern.
- **Durability is "as good as possible," explicitly not held to the
  event log's bar.** Samples are written to durable storage (the same
  three EF Core providers `ADR-001` already supports — a plain
  append-only table is the v1 engine choice, not a dedicated time-series
  database; see Consequences for why that's deliberate, not an oversight),
  but with none of `ADR-019`'s per-row hash-chaining and none of
  `ADR-023`'s persist-everything status envelope — there is no "was this
  sample schema-valid" question to flag, and no per-sample response
  round-trip to make durable-before-responding. This is a real,
  deliberate reduction in guarantee versus the event log, stated
  explicitly rather than silently inherited.
- **Read/replay reuses `ADR-010`'s tail-vs-replay shape directly, applied
  to the telemetry store instead of the event log**: `mode=tail`
  (default — new samples only) or `mode=replay&fromTimestamp=<t>`
  (historical samples from a point forward, then continuing live with no
  gap) — one continuous read loop, only the initial cursor differs,
  exactly the reasoning `ADR-010` already worked out. This is what makes
  "review/replay from any given point at a later date" fall out of a
  decision already made, not a new mechanism.
- **Channels may be `Origin` (raw ingested) or `Derived`** (resampled/
  filtered/aggregated from one or more source channels). A `Derived`
  channel declares `SourceChannelIds` + a `TransformKind`
  (`Resample` | `Filter` | `Aggregate` for `RawScalar`/`RawBinary`;
  `Transcode` for `Media` — e.g. downsampling a video's resolution/
  bitrate, or extracting an audio track — parameters specific to each) and
  is populated by a background `ChannelDerivationWorker` —
  architecturally "an internal follower," the same shape `ADR-007`'s
  derivation workers, `ADR-015`'s `ProjectionHost`, and `ADR-027`'s
  `UpcastMaterializer` all already use: tail the source channel(s) via
  the same read/replay path above, apply the transform, append to the
  derived channel through the same ingestion path any other writer uses.
  No second derivation mechanism invented for telemetry specifically.
- **Detection is explicitly out of framework scope — a detector is an
  application concern, not a core-engine one** (`ADR-030`'s "the core
  engine contains zero domain-specific knowledge" applies here without
  modification: an EEG anomaly detector and a voltage-threshold detector
  have nothing in common algorithmically, so the framework doesn't try to
  provide either). What the framework *does* provide is the bridge: a
  detector process reads a channel (tail or replay, above), and when it
  finds something worth recording, **publishes an ordinary domain event
  through the completely normal publish path** (`ADR-020`/`ADR-023`,
  full schema validation, full persist-everything posture, everything
  else in this design applies unchanged) carrying a new envelope field:
  `TelemetryPointer: { ChannelId, FromTimestamp, ToTimestamp? }`
  (`ToTimestamp` omitted for a single point, present for a window).
- **`TelemetryPointer` is envelope metadata, kept out of `Payload`** —
  the exact same reasoning `ADR-005` already established for
  `parentEventIds`: it must never collide with JSON Schema validation or
  `additionalProperties` rules, and it answers a structurally different
  question ("where in a signal did this come from") than either
  `parentEventIds` ("what event(s) is this causally derived from") or
  `MaterializationOfEventId` ("what is this a re-shaped copy of") — three
  distinct envelope-metadata relationships, three distinct fields,
  never conflated into one overloaded mechanism.
- **Auth reuses existing mechanisms, applied to a new resource type**:
  new scopes `telemetry:ingest`/`telemetry:read` (`ADR-006`'s pattern,
  one policy per operation), and an optional per-channel required claim
  using the exact same `"type:value"` string format `ADR-008` already
  established for event types — no second claim format invented for
  channels.

## Out-of-order/late arrival and slow-upload detection

Two further detection capabilities worth building in, not leaving to
guesswork once a producer misbehaves — both reuse mechanisms this design
already has, applied to `TelemetrySample` instead of `StoredEvent`:

- **Out-of-order/late arrival**: the exact same high-water-mark
  comparison `ADR-029` already established for the Entity Store applies
  here per channel — a channel tracks `LastAppliedLogicalTime` against
  each incoming sample's declared `Timestamp`; a sample whose timestamp
  is behind the channel's high-water mark is flagged
  (`LateArrivalFlag`, the same field name and meaning as `ADR-029`'s,
  not a second flag invented for telemetry) rather than silently
  reordered or silently dropped. For `RawScalar`/`RawBinary` channels
  this is usually diagnostic only (samples are typically consumed in
  timestamp order downstream regardless); for `Media` channels it
  matters more directly, since a genuinely out-of-order frame can break
  decode order for a consumer expecting a monotonic stream.
- **Slow upload / producer lag**: a channel declares an
  `ExpectedInterArrivalInterval` at registration (matching
  `SampleIntervalMicros` for a fixed-rate channel). The ingestion path
  compares the gap between successive batches' receive time against
  that expectation; a channel falling behind by more than a configurable
  threshold triggers the same "detector publishes an ordinary domain
  event" pattern already established above — a reserved
  `ChannelLagDetected` event (system-owned, the same treatment
  `ADR-020`'s `EventUpcastFailed` already gets, not a bespoke alerting
  side-channel), carrying the channel, the expected vs. actual gap, and
  a `TelemetryPointer` at the last sample actually received. This makes
  producer health an ordinary, queryable, `Follow`-able fact rather than
  something only visible in operational logs.

## Playback, deep-linking, redaction, and annotation

Four practices worth naming explicitly, since "just store and replay the
bytes" undersells what's actually needed once `Media` channels are real:

- **Playback** reuses **HTTP Range Requests** (RFC 7233 — the `Range`
  header, `206 Partial Content`, byte-range addressing) for seeking
  within a channel's stored chunks — the standard mechanism that makes a
  video/audio element's scrub bar work, not something this design needs
  to invent. The tail/replay read path (above) serves the *live and
  historical stream*; Range requests serve *random-access seeking within
  what's already been read* — genuinely different questions, both real.
- **Deep-linking** — a stable, shareable reference to a specific
  point/interval within a channel — adopts the **W3C Media Fragments URI**
  spec's temporal fragment syntax (`#t=10,20`, a half-open begin/end
  interval) rather than inventing a bespoke query-parameter scheme. This
  is the same *shape* `TelemetryPointer`'s `{FromTimestamp, ToTimestamp?}`
  already has — adopting the W3C syntax for the *URI* form means a
  deep-link and an internal `TelemetryPointer` are trivially
  interconvertible, not two independent representations of the same idea.
- **Annotation is not a new mechanism — it's simply what a detector
  publishing an event with a `TelemetryPointer` already *is*.** "At 2:15
  the patient reported dizziness" is an ordinary domain event whose
  `TelemetryPointer` names that timestamp — no separate "annotation"
  table or API, the same way this design never needed a separate
  mechanism for "an event about an event" once `parentEventIds` existed.
- **Redaction is genuinely new, not a reuse of `ADR-009`.** Masking wraps
  a JSON *value* in a `{value}`/`{masked}` envelope — meaningless for a
  byte range inside an audio/video/signal chunk. A channel needs its own
  redaction primitive: a declared `RedactedRange` (`ChannelId`,
  `FromTimestamp`, `ToTimestamp`, `RequiredClaim` — reusing `ADR-008`'s
  `"type:value"` claim string, not a new claim format) that a read-time
  transform applies by substituting ~~silence~~/blank frames/zeroed
  samples over that span for a caller lacking the claim.
  **Substitution content refined by
  [`docs/comparisons/streaming-redaction-mechanism.md`](../comparisons/streaming-redaction-mechanism.md)**:
  a distinctive tone, not silence, for audio — per the real forensic
  redaction guidance (SWGDE M-18-001) that a silent redacted span can be
  confused with genuinely silent content, the opposite of what
  redaction is supposed to signal. Zero-fill (not statistical noise)
  for `RawScalar`/`RawBinary` at the core-engine level; a sideband
  "redaction applied here" existence flag is also needed regardless of
  substitution content. The same
  claims-gate-the-*value*-not-the-existence posture `ADR-009` already
  established, just with a redaction *result* appropriate to binary
  content instead of a JSON wrapper. Not designed further than this
  shape here; a full ADR if/when it's actually built.

Consequences:
- **Choosing a plain append-only table over a dedicated time-series
  database (InfluxDB, TimescaleDB, Prometheus) is a real "buy vs. build"
  call, made deliberately, not by default.** A dedicated engine would
  give better compression and downsampling-at-rest for genuinely
  high-volume production telemetry; it would also be a fourth storage
  technology in a design that has otherwise been careful to keep the
  provider count fixed at three (`ADR-001`) and to prefer reusing an
  existing primitive over adding a new dependency (this document's own
  read-path reuse of `ADR-010` is exactly that instinct, applied here).
  Recorded in `references.md` as a real, considered alternative — revisit
  if actual sample-rate/volume in a real deployment makes the plain-table
  engine's limits the binding constraint. `Media` channels specifically
  would, in a real deployment, more naturally sit in object/blob storage
  behind a CDN than in a relational append-only table (large chunk sizes,
  different access pattern) — the same append-only-table engine is
  offered here for design consistency across every `ContentKind`, not
  because it's the recommended production engine for video specifically;
  flagged rather than silently generalized past its actual fit.
- Batch ingestion means a batch is the unit of durability — a crash
  mid-batch can lose that batch's samples, a real, accepted gap given the
  "as good as possible, not held to the event log's bar" framing above.
  A `Derived` channel's `ChannelDerivationWorker` inherits the same
  "replay from `0`, rebuild is cheap" property `ADR-015`'s projections
  already have, so a lost/corrupt derived channel is always recoverable
  by re-deriving from its still-durable source(s) — origin channels are
  the only place a real, unrecoverable gap can occur.
- No hash-chain, no hash hash-based tamper evidence exists for telemetry
  by default (`ADR-019` doesn't extend here) — if a specific deployment
  genuinely needs tamper evidence for raw signal data (e.g. regulated
  medical-device EEG capture), that would need its own decision, layered
  on top, not assumed free from `ADR-019`'s existing mechanism.
- This is a second data plane in the same system, with its own storage,
  own ingestion path, own read path, and its own auth scopes — a real
  increase in surface area, justified specifically because trying to
  make one pipeline serve both discrete business events and high-frequency
  raw signals well would have compromised both.
