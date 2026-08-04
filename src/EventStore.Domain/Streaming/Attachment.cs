namespace EventStore.Domain.Streaming;

// Shape is the data-model authority: docs/data/streaming-and-attachments.md.
// Content-addressed by construction (ADR-032) -- ContentHash is the real
// primary key in spirit, stable regardless of where the bytes physically
// live.
public class Attachment
{
    public string ContentHash { get; set; } = default!;
    public byte[]? Bytes { get; set; }
    public string MimeType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? FileName { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string? ContentProviderKey { get; set; }
    public string? ContentProviderRef { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public List<ChunkRef>? ChunkIndex { get; set; }
    public string? RequiredReadClaim { get; set; }
    public string? RequiredPublishClaim { get; set; }
}

public record ChunkRef(string ChunkHash, long Offset, long Length, string ContentProviderKey, string ContentProviderRef);

// Envelope metadata, the fourth field in the parentEventIds/
// MaterializationOfEventId/TelemetryPointer family (ADR-032) -- answers
// "supporting document for," a distinct relationship from all three.
// EntityId/EventId are both independently optional; either, both, or
// neither may be set (a standalone attachment with no link at all).
public class AttachmentRef
{
    public long Id { get; set; }
    public string ContentHash { get; set; } = default!;
    public string? EntityId { get; set; }
    public Guid? EventId { get; set; }
}
