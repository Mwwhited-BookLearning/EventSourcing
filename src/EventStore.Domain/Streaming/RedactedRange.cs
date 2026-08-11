namespace EventStore.Domain.Streaming;

// Field shape named in ADR-031; the read-time substitution mechanism and
// Strategy field resolved in ADR-052. Composite PK (ChannelId, FromTimestamp).
public class RedactedRange
{
    public string ChannelId { get; set; } = default!;
    public DateTimeOffset FromTimestamp { get; set; }
    public DateTimeOffset ToTimestamp { get; set; }
    public string RequiredClaim { get; set; } = default!;
    public string Strategy { get; set; } = "Default";
    public int? ShowFirst { get; set; }
    public int? ShowLast { get; set; }
    public char? MaskChar { get; set; }
    public bool PreserveSeparators { get; set; }
}
