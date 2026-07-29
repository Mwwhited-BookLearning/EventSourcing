[← Data model index](../02-data-model.md)

# Streaming Channels & Binary Attachments

A deliberately **separate** data plane from the event log and Entity
Store (`event-log.md`, `entity-store.md`) — see `ADR-031`/`ADR-032` for
why forcing high-frequency signal/media data or discrete binary uploads
through the same schema-validated, hash-chained, fold-into-Entity-Store
pipeline would make that pipeline's own correctness machinery the
bottleneck for data that doesn't need most of it.

```csharp
public class TelemetryChannel
{
    public string ChannelId { get; set; } = default!;   // PK
    public string AppId { get; set; } = default!;        // ADR-030
    public string EntityId { get; set; } = default!;     // ADR-021 -- which entity this channel belongs to
    public ContentKind ContentKind { get; set; }          // RawScalar | RawBinary | Media
    public SampleType? SampleType { get; set; }           // Float64 | Int32 -- only for RawScalar
    public string? MimeType { get; set; }                 // e.g. "audio/opus", "video/h264" -- only for Media
    public long? SampleIntervalMicros { get; set; }        // fixed-rate channels only; also this channel's ExpectedInterArrivalInterval (ADR-031)
    public ChannelOrigin Origin { get; set; }              // Origin | Derived
    public List<string>? SourceChannelIds { get; set; }    // Derived channels only
    public string? TransformKind { get; set; }             // Resample | Filter | Aggregate | Transcode -- Derived channels only
    public string? RequiredReadClaim { get; set; }         // reuses ADR-008's "type:value" format, applied to a channel instead of an event type
}

public enum ContentKind { RawScalar, RawBinary, Media }
public enum SampleType { Float64, Int32 }
public enum ChannelOrigin { Origin, Derived }

public class TelemetrySample
{
    public string ChannelId { get; set; } = default!;
    public DateTimeOffset Timestamp { get; set; }          // CLIENT-DECLARED, same discipline as StoredEvent.OccurredAt (ADR-029)
    public byte[] Value { get; set; } = default!;           // a scalar, an opaque blob, or a codec frame, per ContentKind
    public bool LateArrivalFlag { get; set; }               // ADR-029's mechanism, reused per-channel (ADR-031)
}

public class RedactedRange
{
    public string ChannelId { get; set; } = default!;
    public DateTimeOffset FromTimestamp { get; set; }
    public DateTimeOffset ToTimestamp { get; set; }
    public string RequiredClaim { get; set; } = default!;   // reuses ADR-008's "type:value" format
    public string Strategy { get; set; } = "Default";        // "Default" (zero-fill for RawScalar/RawBinary, tone for audio, blank frame for video -- ADR-052) | "PartialReveal" (ADR-009's strategy, reused -- only meaningful for structured/string-shaped content, never a raw waveform or video frame)
    public int? ShowFirst { get; set; }                      // PartialReveal only -- named fields, not a mask-template string (ADR-009)
    public int? ShowLast { get; set; }                       // PartialReveal only
    public char? MaskChar { get; set; }                      // PartialReveal only, defaults to 'X'
    public bool PreserveSeparators { get; set; }              // PartialReveal only -- literal non-alphanumeric characters show through untouched
}

// Content-addressed -- ContentHash is the real primary key in spirit,
// EntityId/EventId here are just the most common lookup paths.
public class Attachment
{
    public string ContentHash { get; set; } = default!;    // SHA-256 of the raw bytes -- PK
    public byte[] Bytes { get; set; } = default!;
    public string MimeType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? FileName { get; set; }                   // optional, display/WebDAV-resource-name only -- never part of the address
    public DateTimeOffset UploadedAt { get; set; }
}

public class AttachmentRef
{
    public string ContentHash { get; set; } = default!;     // FK -> Attachment
    public string? EntityId { get; set; }                    // ADR-021 -- either/both may be set
    public Guid? EventId { get; set; }                        // -- links to a specific event, not just its entity generally
}
```

## Why these aren't `StoredEvent`/`EntityStoreRow`

- No `JsonSchema` validation at any `ContentKind`/`MimeType` — structural
  checking is exactly the per-item cost this whole data plane exists to
  avoid (`ADR-031`).
- No `ChainHash` per sample and no per-attachment hash chain — tamper
  evidence (`ADR-019`) doesn't extend here by default; `ContentHash`
  gives content-equality/deduplication, a different guarantee (`ADR-032`).
- `TelemetrySample` is **never folded** into an `EntityStoreRow` the way
  `StoredEvent` is — a detector reading a channel and finding something
  worth recording publishes an ordinary `StoredEvent` (with a
  `TelemetryPointer` envelope field, not shown as a column here since it
  lives on `StoredEvent` in `event-log.md`) — that event *is* what gets
  folded, not the raw samples themselves.

## Access

`ADR-031`'s tail/replay reuses `ADR-010`'s Follow shape, applied to
`TelemetrySample` instead of `StoredEvent`. `ADR-032`'s attachments are
additionally browsable via WebDAV (RFC 4918) — a virtual collection/
resource hierarchy projected over `Attachment`/`AttachmentRef`, detailed
in `ADR-032` itself rather than here, since it's an access-pattern
decision, not a storage-shape one.
