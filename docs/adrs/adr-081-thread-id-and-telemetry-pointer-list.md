[← ADR index](../07-adrs.md)

# ADR-081: `TelemetryChannel.ThreadId` for multi-channel session grouping, and `TelemetryPointer` generalized to a list — revises `ADR-031`

Status: Accepted — revises `ADR-031`

Context: `docs/10-open-questions.md` raised two related, concrete gaps in
`ADR-031`'s streaming-channel design, both surfaced by a real multi-
channel device-telemetry case (a 32-electrode EEG montage — one
recording session, 32 simultaneous `ChannelId`s): (1) nothing groups
multiple simultaneous channels under one logical session/recording, and
(2) `TelemetryPointer` is singular (`{ChannelId, FromTimestamp,
ToTimestamp?}`), so a detector whose finding spans a *correlated pattern
across multiple channels at once* (e.g. a seizure signature visible
across several EEG channels simultaneously) has no way to name more than
one channel's window in a single detection event. Direct design
conversation resolved both — **yes** to both proposed shapes.

Decision:
- **`TelemetryChannel` gains `ThreadId`** (`docs/data/streaming-and-
  attachments.md`) — an optional string grouping multiple simultaneous
  channels registered together as one logical session/recording (the
  32-channel EEG montage case). Named `ThreadId` specifically, not
  `StreamId` — `ADR-021` deliberately retired `StreamId` terminology when
  entities became first-class, and reusing it here would silently
  resurrect exactly the ambiguity that retirement was meant to prevent.
  `ThreadId` has no meaning of its own beyond grouping; it is not itself
  an `EntityId`, a channel, or a stream — purely a denormalized
  correlation key.
- **`ThreadId` is denormalized onto `TelemetryPointer` too**, not just
  `TelemetryChannel` — so a flat query across every event pointing into
  one recording session ("every detection during EEG session X") never
  needs to join back through `TelemetryChannel` to find out which
  channels belong to which session. The same denormalize-for-flat-query
  reasoning `docs/data/entity-store.md`'s `ShardKey`/other denormalized
  fields already use, applied here.
- **`TelemetryPointer` generalizes from a single object to a list of
  entries** — `List<TelemetryPointerEntry>`, each entry carrying
  `{ChannelId, ThreadId?, FromTimestamp, ToTimestamp?}`. An ordinary
  single-channel detection is simply a one-entry list, not a different
  shape branching on cardinality — no `TelemetryPointer` vs.
  `TelemetryPointers` field split, one shape handles both cases. A
  detection triggered by a correlated pattern across multiple channels
  publishes one event with multiple entries, one per contributing
  channel's window.
- **The actual triggering values/thresholds stay in `Payload`, never
  promoted into `TelemetryPointerEntry`** — confirmed, not a new
  decision: `TelemetryPointer`'s job is strictly "where in a signal did
  this come from," the same distinction `ADR-031` already drew between
  envelope metadata and event content. A correlation score, a per-channel
  amplitude, a confidence value are all ordinary, event-type-specific
  `Payload` fields.
- **Still envelope metadata, still a `TEXT` column, no new table** —
  `TelemetryPointer` remains a single JSON-serialized column on
  `StoredEvent` (`ADR-004`'s portability rule); only the JSON *shape*
  inside that column changes, from one object to an array of objects.
  Not a relational join table like `parentEventIds`'s `EventParent` —
  this relationship doesn't need independent queryability the way
  lineage traversal does, so the lighter-weight shape stays appropriate.

Consequences:
- `docs/data/streaming-and-attachments.md`'s `TelemetryChannel` class and
  `docs/data/event-log.md`'s `TelemetryPointer` comment are updated in
  this same pass, per this project's data-model-ownership convention
  (`CLAUDE.md`).
- **Every existing example showing the old singular `TelemetryPointer`
  shape (a bare object, not a one-element array) is now stale** — real,
  not cosmetic, propagation debt across `docs/features/*.md` and
  multiple domains' feature docs (at minimum the clinical-trials-device-
  telemetry and industrial-iot-predictive-maintenance domains' worked
  examples). Tracked in `TODO.md`, not fixed in this pass — a mechanical
  sweep, not a design question.
- `ADR-031` itself gets a struck-through pointer at the specific revised
  bullet (`TelemetryPointer`'s shape), per `.claude/protocols/additive-
  history-editing.md` — the rest of `ADR-031`'s decision is unaffected.
- Resolves `docs/10-open-questions.md` row 20.
