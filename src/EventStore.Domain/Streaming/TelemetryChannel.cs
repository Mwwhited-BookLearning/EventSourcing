namespace EventStore.Domain.Streaming;

// Shape is the data-model authority: docs/data/streaming-and-attachments.md.
// A deliberately separate data plane from StoredEvent/EntityStoreRow --
// ADR-031: no JsonSchema, no ChainHash, no Entity Store fold, at any
// ContentKind.
public class TelemetryChannel
{
    public string ChannelId { get; set; } = default!;
    public string AppId { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public ContentKind ContentKind { get; set; }
    public SampleType? SampleType { get; set; }
    public string? MimeType { get; set; }
    public long? SampleIntervalMicros { get; set; }
    public ChannelOrigin Origin { get; set; }
    public string? ThreadId { get; set; }
    public List<string>? SourceChannelIds { get; set; }
    public string? TransformKind { get; set; }
    public string? RequiredReadClaim { get; set; }

    public DateTimeOffset LastAppliedLogicalTime { get; set; }
    public DateTimeOffset? LastBatchReceivedAt { get; set; }
    public DateTimeOffset? LastSampleTimestampReceived { get; set; }
}

public enum ContentKind { RawScalar, RawBinary, Media }
public enum SampleType { Float64, Int32 }
public enum ChannelOrigin { Origin, Derived }
