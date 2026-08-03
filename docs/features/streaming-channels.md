# Feature: Streaming channels (telemetry, audio/video ingestion)

Context: decision record `ADR-031` in `../07-adrs.md`; entities in
`../data/streaming-and-attachments.md`; the `TelemetryPointer` envelope
field on `StoredEvent` is defined in `../data/event-log.md` and takes its
place alongside `parentEventIds` (`ADR-005`, [`event-chains.md`](event-chains.md))
and `MaterializationOfEventId` (`ADR-027`) as a distinct envelope-metadata
relationship — this doc covers only the parts specific to streaming
channels. A detector's published event goes through the completely normal
publish path once it decides to record something — see
[`publish-event.md`](publish-event.md) for that pipeline itself (schema
validation, `ADR-023`'s persist-everything `202`+`SchemaStatus` posture);
this doc only adds the `telemetryPointer` envelope field on top of it.
Auth requirements (new `telemetry:ingest`/`telemetry:read` scopes,
`ADR-006`'s pattern) are in [`auth.md`](auth.md). Pattern write-ups for
the mechanisms this feature reuses rather than invents — streaming
ingestion as a separate fast path, deep-linking via temporal fragment
URIs, seekable playback via byte-range requests — are catalogued in
`../patterns/README.md`'s "Decided, not yet written up as standalone
docs" table. `RedactedRange`'s read-time redaction transform, originally
left unspecified beyond its field shape, is now resolved in `ADR-052`
(read-time, zero-fill/tone/blank-frame default per `ContentKind`, a
configurable `Strategy`) — see `../comparisons/streaming-redaction-mechanism.md`
for the full prior-art search and reasoning. `ADR-052`'s existence-signal
requirement — every `RedactedRange` application also sets a sideband
flag at the read/tail/replay boundary, structurally the same shape
`TelemetrySample.LateArrivalFlag` already uses — means a caller lacking
the claim always learns *that* redaction applied, never just seeing
substituted content that looks like ordinary data. `ADR-081` revises
`ADR-031` further, on two points this doc's scenarios now cover:
`TelemetryChannel` gains `ThreadId`, an optional grouping key for
multiple simultaneous channels registered together as one logical
session/recording (a multi-electrode montage is the motivating case) —
denormalized onto `TelemetryPointer` too, so a flat query across every
event pointing into one session never has to join back through
`TelemetryChannel`; and `TelemetryPointer` generalizes from a single
object to a `List<TelemetryPointerEntry>` (each entry `{ChannelId,
ThreadId?, FromTimestamp, ToTimestamp?}`), so a detection spanning a
correlated pattern across several channels at once can name every
contributing channel's window in one event — an ordinary single-channel
detection is simply a one-entry list, not a different shape.

The `Streaming Channel Service` container appears in
`../01-c4-architecture.md` (its component diagram is flagged there as
still outstanding — the container itself, and everything this doc
describes, is real); build sequencing is "Streaming Channels" in
`../08-build-plan.md`.
This is the first feature doc for `ADR-031` — there is no prior version to
supersede, so unlike several other `features/*.md` files this one carries
no stale-scenario banner.

## Sequence diagram — ingesting a batch of samples

```plantuml
@startuml Streaming_Ingest_Sequence
autonumber
actor "Telemetry Producer" as producer
participant "Telemetry Ingest API" as endpoint
participant "Auth\n(JWT Bearer + scope policy)" as auth
participant "ChannelRegistry" as registry
participant "TelemetrySampleWriter" as writer
database "Telemetry Store" as db

producer -> endpoint: POST /telemetry/{channelId}/samples\nAuthorization: Bearer <JWT>\n{ startTimestamp, sampleIntervalMicros, values: [...] } (fixed-rate)\n-- or { samples: [{timestamp, value}, ...] } (irregular)
endpoint -> auth: validate token + telemetry:ingest scope
alt missing/invalid token
  auth --> producer: 401
else valid token, missing scope
  auth --> producer: 403
else authorized
  endpoint -> registry: lookup ChannelId
  alt channel unknown
    registry --> producer: 404
  else channel found
    endpoint -> writer: append(batch)
    loop for each sample in batch
      writer -> writer: compare Timestamp to channel's LastAppliedLogicalTime\n(ADR-029's mechanism, reused per-channel -- ADR-031)
      alt Timestamp < LastAppliedLogicalTime
        writer -> writer: set LateArrivalFlag = true\n(sample is still written -- never dropped, never reordered)
      end
    end
    writer -> writer: compare this batch's receive-time gap\nto the channel's ExpectedInterArrivalInterval
    alt gap exceeds configured lag threshold
      writer -> writer: schedule a ChannelLagDetected publish\n(async, via the normal publish path -- see diagram below)
    end
    writer -> db: INSERT TelemetrySample rows\n(no JsonSchema check, no ChainHash, no Entity Store fold)
    writer -> writer: advance LastAppliedLogicalTime to max(Timestamp)\nacross non-late-flagged samples in this batch
    writer --> producer: 202 { channelId, samplesWritten, lateArrivalCount }
  end
end
@enduml
```

## Sequence diagram — a detector publishing a linked event

```plantuml
@startuml Streaming_Detector_Publish_Sequence
autonumber
participant "Detector Worker\n(app-specific, out of framework scope)" as detector
participant "Telemetry Read API" as read
database "Telemetry Store" as tdb
participant "Publish API" as publish
participant "SchemaValidationService" as validator
participant "EventAppender" as appender
database "Event & Schema Store" as edb

detector -> read: GET /telemetry/{channelId}/samples?mode=tail\n(or mode=replay&fromTimestamp=<t> -- ADR-010's shape, reused as-is)
read -> tdb: SELECT ... WHERE Timestamp > lastSeen
tdb --> read: TelemetrySample rows
read --> detector: streamed samples
detector -> detector: apply domain-specific detection logic\n(an EEG anomaly rule, a voltage threshold, ... -- ADR-031 takes no position)
alt something worth recording is found
  detector -> publish: POST /publish/{event-type}\n{ payload, telemetryPointer: [{ channelId, fromTimestamp, toTimestamp? }] }
  publish -> validator: validate(payload, schema)
  validator --> publish: SchemaStatus (conformant | invalid | unknown -- ADR-023, never blocks the write)
  publish -> appender: append(StoredEvent { ..., TelemetryPointer })
  appender -> edb: INSERT StoredEvent\n(the completely normal publish pipeline -- ADR-020/ADR-023, unchanged)
  appender --> detector: 202 { correlationId, status, schemaStatus, entityId }
else nothing found this poll
  detector -> detector: continue tailing
end
@enduml
```

`TelemetryPointer` is envelope metadata, never `Payload` — the same reasoning
`ADR-005` already established for `parentEventIds`, so it can never collide
with `additionalProperties`/JSON Schema validation.

## Sequence diagram — a correlated detection across a `ThreadId`-grouped session

```plantuml
@startuml Streaming_ThreadId_Correlated_Detection_Sequence
autonumber
participant "Detector Worker" as detector
participant "Telemetry Read API" as read
database "Telemetry Store" as tdb
participant "Publish API" as publish
participant "EventAppender" as appender
database "Event & Schema Store" as edb

note over detector, tdb
  32-electrode EEG montage: channels "eeg-ch1".."eeg-ch32" were all
  registered with ThreadId "session-9f2" (ADR-081) -- one logical
  recording, 32 simultaneous ChannelIds
end note

detector -> read: GET /telemetry/sessions/session-9f2/samples?mode=tail\n(ThreadId-scoped tail -- ADR-081)
read -> tdb: SELECT ... WHERE ChannelId IN (SELECT ChannelId FROM TelemetryChannel WHERE ThreadId = 'session-9f2')\nAND Timestamp > lastSeen
tdb --> read: TelemetrySample rows across all 32 channels, grouped by ThreadId
read --> detector: one grouped session view (not 32 unrelated channel streams)
detector -> detector: apply correlation logic across channels\n(a seizure signature visible on several channels at once)
alt correlated pattern found across channels "eeg-ch3", "eeg-ch7", "eeg-ch12"
  detector -> publish: POST /publish/SeizureSignatureDetected\n{ payload: {...}, telemetryPointer: [\n    { channelId: "eeg-ch3", threadId: "session-9f2", fromTimestamp: t0, toTimestamp: t1 },\n    { channelId: "eeg-ch7", threadId: "session-9f2", fromTimestamp: t0, toTimestamp: t1 },\n    { channelId: "eeg-ch12", threadId: "session-9f2", fromTimestamp: t0, toTimestamp: t1 } ] }
  publish -> appender: append(StoredEvent { ..., TelemetryPointer })\n(List<TelemetryPointerEntry>, one entry per contributing channel -- ADR-081)
  appender -> edb: INSERT StoredEvent
  appender --> detector: 202 { correlationId, status, schemaStatus, entityId }
else no cross-channel pattern found this poll
  detector -> detector: continue tailing the grouped session
end
@enduml
```

`ThreadId` has no meaning of its own beyond grouping (`ADR-081`) — it is
not an `EntityId`, a channel, or a stream, purely a denormalized
correlation key present on both `TelemetryChannel` and every
`TelemetryPointerEntry` that points into a channel belonging to that
session. A single-channel detection (the diagram above the previous
section) is simply a one-entry `TelemetryPointer` list; this diagram is
the same mechanism with more entries, not a different shape.

## Sequence diagram — tail/replay with a `RedactedRange` applied

```plantuml
@startuml Streaming_RedactedRange_Read_Sequence
autonumber
actor "Follower" as follower
participant "Telemetry Read API" as read
participant "Auth\n(JWT Bearer + claim check)" as auth
database "Telemetry Store" as tdb
participant "IStreamRedactionStrategy\n(ADR-052)" as strategy

follower -> read: GET /telemetry/eeg-ch1/samples?mode=replay&fromTimestamp=<t>
read -> tdb: SELECT TelemetrySample WHERE ChannelId = 'eeg-ch1' AND Timestamp >= <t>
read -> tdb: SELECT RedactedRange WHERE ChannelId = 'eeg-ch1'\nAND [FromTimestamp, ToTimestamp] overlaps requested window
tdb --> read: overlapping RedactedRange { FromTimestamp, ToTimestamp, RequiredClaim, Strategy }
read -> auth: does Follower hold RedactedRange.RequiredClaim?
alt Follower holds the claim
  auth --> read: authorized for this range
  read --> follower: real sample values for the full window,\nincluding the RedactedRange span
else Follower lacks the claim
  auth --> read: not authorized for this range
  read -> strategy: resolve the configured IStreamRedactionStrategy\n(Strategy == "Default" -> ZeroFillStrategy/ToneStrategy/BlankFrameStrategy\nper ContentKind; Strategy == "PartialReveal" -> reuses\nPartialRevealMaskingStrategy's reveal computation, ADR-009/ADR-052)
  strategy --> read: substituted bytes for the RedactedRange span only\n(samples outside the span are returned unmodified)
  read --> follower: samples for the window, with the RedactedRange span\nreplaced by the substitution AND a sideband existence flag set\n(same shape as TelemetrySample.LateArrivalFlag) --\nnever a response indistinguishable from "no redaction happened here"
end
@enduml
```

Samples outside a `RedactedRange`'s span are never touched by this path —
only the overlapping span is substituted, and the substitution is a
read-time transform (`ADR-052`): `TelemetrySample` rows on disk are
untouched, so a caller who later acquires the claim (or a caller who
already holds it) sees the same real underlying data, not a
once-redacted-forever copy.

## Data model (ER diagram)

```plantuml
@startuml StreamingChannels_ER
hide circle
skinparam linetype ortho

entity "TelemetryChannel" as channel {
  * ChannelId : string <<PK>>
  --
  AppId : string
  EntityId : string
  ContentKind : enum {RawScalar, RawBinary, Media}
  SampleType : enum {Float64, Int32}?
  MimeType : string?
  SampleIntervalMicros : bigint?
  Origin : enum {Origin, Derived}
  ThreadId : string?
  SourceChannelIds : string[]?
  TransformKind : enum {Resample, Filter, Aggregate, Transcode}?
  RequiredClaims : {Direction, Claim}[]?
}

entity "TelemetrySample" as sample {
  * ChannelId : string <<PK, FK>>
  * Timestamp : datetimeoffset <<PK>>
  --
  Value : bytes
  LateArrivalFlag : bool
}

entity "RedactedRange" as redaction {
  * ChannelId : string <<PK, FK>>
  * FromTimestamp : datetimeoffset <<PK>>
  --
  ToTimestamp : datetimeoffset
  RequiredClaim : string
  Strategy : string = "Default"
  ShowFirst : int?
  ShowLast : int?
  MaskChar : char?
  PreserveSeparators : bool
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  TelemetryPointer : string?
}

channel ||--o{ sample : "ChannelId -- real FK,\nevery sample belongs to a declared channel"
channel ..o{ redaction : "ChannelId -- field shape (ADR-031),\nread-time substitution mechanism\nand Strategy field resolved in ADR-052"
channel ..> channel : "SourceChannelIds -- a Derived channel references\none or more source channels; a string list,\nnot a normal FK"
channel ..> channel : "ThreadId -- multiple channels registered together\nshare one ThreadId value; a denormalized grouping\nkey (ADR-081), not a normal FK"
event ..> channel : "TelemetryPointer.ChannelId -- logical only,\nNOT a DB FK; TelemetryPointer itself is a\ncolumn on StoredEvent, defined in event-log.md.\nJSON-serialized List<TelemetryPointerEntry>\n{ChannelId, ThreadId?, FromTimestamp, ToTimestamp?}\nper entry (ADR-081) -- one entry for an ordinary\nsingle-channel detection, multiple for a correlated\nmulti-channel one; ThreadId is denormalized onto\neach entry too, not just onto TelemetryChannel"

note right of sample
  Composite PK (ChannelId, Timestamp).
  No ChainHash, no JsonSchema check, no
  Entity Store fold -- exactly the per-item
  cost this data plane exists to avoid.
end note

note bottom of redaction
  Field shape named in ADR-031.
  The read-time transform that substitutes
  zero-fill/tone/blank-frame content (Strategy
  "Default", per ContentKind) or a format-preserving
  partial reveal (Strategy "PartialReveal", reusing
  PartialRevealMaskingStrategy -- ADR-009) over this
  span is resolved in ADR-052 --
  see comparisons/streaming-redaction-mechanism.md.
  Every application also sets a sideband existence
  flag at the read boundary (ADR-052) -- not shown
  as its own column, structurally the same as
  TelemetrySample.LateArrivalFlag.
end note

note bottom of channel
  ThreadId groups multiple simultaneous channels
  registered together as one logical session/
  recording (e.g. a 32-electrode EEG montage) --
  ADR-081. No meaning of its own beyond grouping;
  not an EntityId, a channel, or a stream.
end note
@enduml
```

Full entity set is in `../data/streaming-and-attachments.md`; this diagram
shows only what ingestion, detection, and the redaction shape actually
touch. `Attachment`/`AttachmentRef` (`ADR-032`) are a related but distinct
data plane — discrete binary objects, not a sequenced stream — and are out
of scope for this doc.

## Salt (UI mockup)

Not applicable — this is a data-ingestion/API feature (telemetry ingest,
tail/replay, playback, detector publish) with no UI surface in scope.

## Gherkin

```gherkin
Feature: Streaming channels (telemetry, audio/video ingestion)
  As a telemetry producer, a detector worker, or a playback client
  I want raw signal/media samples ingested and read back on a fast path
  kept separate from the event log
  So that high-frequency signal data never pays for schema validation,
  hash-chaining, or an Entity Store fold it doesn't need -- and a detector
  can still bridge back to an ordinary, fully-validated domain event when
  something in the stream is actually worth recording

  # Every request in this file carries a Bearer token with sufficient scope
  # (telemetry:ingest for ingestion, telemetry:read for tail/replay/playback,
  # events:publish for a detector's published event) unless a scenario says
  # otherwise. See auth.md for authentication/authorization behavior itself.

  Background:
    Given the entity "patient:123" exists (ADR-021)
    And the event type "DizzinessReported" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Note": { "type": "string" } }, "required": ["Note"] }
      """
    And a "RawScalar" TelemetryChannel "eeg-ch1" is registered for entity "patient:123"
      with SampleType "Float64", SampleIntervalMicros 4000, Origin "Origin"

  Scenario: Registering a new origin channel
    When I PUT "/telemetry/channels/eeg-ch2" with body:
      """
      { "appId": "clinical-app", "entityId": "patient:123", "contentKind": "RawScalar",
        "sampleType": "Float64", "sampleIntervalMicros": 4000, "origin": "Origin" }
      """
    Then the response status should be 201
    And channel "eeg-ch2" should immediately accept ingestion and tail/replay requests

  Scenario: Ingesting a fixed-rate batch omits per-sample timestamps
    When I POST to "/telemetry/eeg-ch1/samples" with body:
      """
      { "startTimestamp": "2026-07-29T10:00:00Z", "sampleIntervalMicros": 4000,
        "values": [0.12, 0.15, 0.11, 0.20] }
      """
    Then the response status should be 202
    And 4 TelemetrySample rows should be written to channel "eeg-ch1"
    And none of those samples should have been JSON-Schema validated, hash-chained, or folded into the Entity Store

  Scenario: A detector publishes an ordinary event carrying a TelemetryPointer
    Given channel "eeg-ch1" has samples spanning "2026-07-29T10:00:00Z" to "2026-07-29T10:00:10Z"
    And a detector tailing channel "eeg-ch1" (mode=tail) notices an anomaly at "2026-07-29T10:00:04Z"
    When the detector POSTs to "/publish/DizzinessReported" with body:
      """
      { "payload": { "Note": "patient reported dizziness" },
        "telemetryPointer": [{ "channelId": "eeg-ch1", "fromTimestamp": "2026-07-29T10:00:04Z" }] }
      """
    Then the response status should be 202
    And the stored event's TelemetryPointer should reference channel "eeg-ch1" at "2026-07-29T10:00:04Z"
    And the stored event should have gone through the normal publish pipeline unchanged, including SchemaStatus

  Scenario: Registering multiple channels under one ThreadId groups them into a session (ADR-081)
    Given a "RawScalar" TelemetryChannel "eeg-ch2" is registered for entity "patient:123"
      with SampleType "Float64", SampleIntervalMicros 4000, Origin "Origin", ThreadId "session-9f2"
    When I PUT "/telemetry/channels/eeg-ch3" with body:
      """
      { "appId": "clinical-app", "entityId": "patient:123", "contentKind": "RawScalar",
        "sampleType": "Float64", "sampleIntervalMicros": 4000, "origin": "Origin",
        "threadId": "session-9f2" }
      """
    Then the response status should be 201
    And channels "eeg-ch2" and "eeg-ch3" should both belong to ThreadId "session-9f2"

  Scenario: A follower querying by ThreadId gets one grouped view across every channel in that session
    Given channels "eeg-ch2" and "eeg-ch3" both belong to ThreadId "session-9f2"
    And both channels have samples spanning "2026-07-29T10:00:00Z" to "2026-07-29T10:00:05Z"
    When I GET "/telemetry/sessions/session-9f2/samples?mode=tail"
    Then the response status should be 200
    And the response should include samples from both "eeg-ch2" and "eeg-ch3"
    And the response should present them as one grouped session view, not two unrelated channel streams

  Scenario: A correlated multi-channel detection publishes a TelemetryPointer with one entry per contributing channel
    Given channels "eeg-ch2" and "eeg-ch3" both belong to ThreadId "session-9f2"
    And a detector tailing ThreadId "session-9f2" notices a pattern correlated across both channels
      between "2026-07-29T10:00:01Z" and "2026-07-29T10:00:02Z"
    When the detector POSTs to "/publish/DizzinessReported" with body:
      """
      { "payload": { "Note": "correlated pattern across eeg-ch2/eeg-ch3" },
        "telemetryPointer": [
          { "channelId": "eeg-ch2", "threadId": "session-9f2", "fromTimestamp": "2026-07-29T10:00:01Z", "toTimestamp": "2026-07-29T10:00:02Z" },
          { "channelId": "eeg-ch3", "threadId": "session-9f2", "fromTimestamp": "2026-07-29T10:00:01Z", "toTimestamp": "2026-07-29T10:00:02Z" }
        ] }
      """
    Then the response status should be 202
    And the stored event's TelemetryPointer should contain exactly two entries, one for "eeg-ch2" and one for "eeg-ch3"
    And both entries should carry ThreadId "session-9f2"
    And the event should have gone through the normal publish pipeline unchanged, including SchemaStatus
      (ADR-081 generalizes the TelemetryPointer shape only, never the pipeline)

  Scenario: Retrieving a byte range from a Media channel for seekable playback
    Given a "Media" TelemetryChannel "cam-ch1" with MimeType "video/h264" has 30 seconds of ingested chunks
    When I GET "/telemetry/cam-ch1/samples" with header "Range: bytes=1000-1999"
    Then the response status should be 206
    And the response should include a "Content-Range" header
    And the response body should contain exactly the requested byte range

  Scenario: A deep-link temporal fragment resolves to the same window as a TelemetryPointer
    Given a "Media" TelemetryChannel "cam-ch1" has ingested chunks spanning at least 20 seconds
    When I resolve the deep link "/telemetry/cam-ch1#t=10,20"
    Then the resolved window should be equivalent to a TelemetryPointer of
      { "channelId": "cam-ch1", "fromTimestamp": "<start+10s>", "toTimestamp": "<start+20s>" }

  Scenario: A late-arriving sample is flagged, not silently reordered or dropped
    Given channel "eeg-ch1"'s high-water mark (LastAppliedLogicalTime) is "2026-07-29T10:00:10Z"
    When I POST to "/telemetry/eeg-ch1/samples" with a sample timestamped "2026-07-29T10:00:05Z"
    Then the response status should be 202
    And that sample should still be written to channel "eeg-ch1"
    And that sample's LateArrivalFlag should be true
    And channel "eeg-ch1"'s high-water mark should remain "2026-07-29T10:00:10Z"

  Scenario: A slow-uploading producer triggers a ChannelLagDetected event
    Given channel "eeg-ch1" has an ExpectedInterArrivalInterval matching its SampleIntervalMicros
    And the gap since channel "eeg-ch1"'s last received batch already exceeds the configured lag threshold
    When the next batch for channel "eeg-ch1" is finally received
    Then a system-owned "ChannelLagDetected" event should be published
    And that event's TelemetryPointer should reference the last sample actually received before the gap
    And that event's payload should carry the expected vs. actual inter-arrival gap

  Scenario: A derived channel is resampled from its source channel
    Given a "Derived" TelemetryChannel "eeg-ch1-1hz" declares SourceChannelIds ["eeg-ch1"] and TransformKind "Resample"
    And channel "eeg-ch1" has 250 samples at a 4000-microsecond interval spanning 1 second
    When the ChannelDerivationWorker tails channel "eeg-ch1" and applies the "Resample" transform
    Then channel "eeg-ch1-1hz" should contain the resampled output for that 1-second span
    And if channel "eeg-ch1-1hz" were lost or corrupted, it should be fully recoverable by re-deriving from "eeg-ch1"

  Scenario: A follower lacking a RedactedRange's required claim receives the substitution plus a sideband existence flag (ADR-052)
    Given channel "eeg-ch1" has a RedactedRange from "2026-07-29T10:00:04Z" to "2026-07-29T10:00:06Z"
      requiring claim "clinical:full-eeg"
    And channel "eeg-ch1" has samples spanning "2026-07-29T10:00:00Z" to "2026-07-29T10:00:10Z"
    And I hold a valid "telemetry:read" scoped token without claim "clinical:full-eeg"
    When I GET "/telemetry/eeg-ch1/samples?mode=replay&fromTimestamp=2026-07-29T10:00:00Z"
    Then the response status should be 200
    And samples outside "2026-07-29T10:00:04Z" to "2026-07-29T10:00:06Z" should contain their real recorded values
    And samples within that range should contain the configured "Default" substitution (zero-fill, since ContentKind is RawScalar)
    And each substituted sample should carry the redaction sideband existence flag set
    And no sample in the response should be indistinguishable from an ordinary, unredacted sample

  Scenario: A follower holding the required claim receives the real content, not the substitution
    Given channel "eeg-ch1" has a RedactedRange from "2026-07-29T10:00:04Z" to "2026-07-29T10:00:06Z"
      requiring claim "clinical:full-eeg"
    And channel "eeg-ch1" has samples spanning "2026-07-29T10:00:00Z" to "2026-07-29T10:00:10Z"
    And I hold a valid "telemetry:read" scoped token with claim "clinical:full-eeg"
    When I GET "/telemetry/eeg-ch1/samples?mode=replay&fromTimestamp=2026-07-29T10:00:00Z"
    Then the response status should be 200
    And every sample in the response, including "2026-07-29T10:00:04Z" to "2026-07-29T10:00:06Z", should contain its real recorded value
    And no sample in the response should carry the redaction sideband existence flag

  Scenario: A RedactedRange configured for PartialReveal substitutes a format-preserving partial value on structured content
    Given a "RawBinary" TelemetryChannel "device-log-ch1" carries structured, string-like records
    And channel "device-log-ch1" has a RedactedRange requiring claim "pii:view"
      with Strategy "PartialReveal", ShowFirst 0, ShowLast 4
    And I hold a valid "telemetry:read" scoped token without claim "pii:view"
    When I GET "/telemetry/device-log-ch1/samples?mode=replay&fromTimestamp=<redacted-range-start>"
    Then the response status should be 200
    And the redacted record's revealed value should show only its last 4 characters, e.g. "XXX-XX-1234"
      (reusing PartialRevealMaskingStrategy's reveal computation -- ADR-009/ADR-052)
    And the redaction sideband existence flag should be set for that record
```
