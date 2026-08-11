namespace EventStore.Timestamping;

public class TimestampingOptions
{
    // The RFC 3161 TSA endpoint URL (e.g. a public TSA, or an internally-
    // operated one for a regulated deployment that can't send hashes to a
    // third party -- ADR-086's own stated reason this is pluggable).
    public string? TsaUrl { get; set; }
}
