namespace EventStore.Domain.Streaming;

// Composite PK (ChannelId, Timestamp). No ChainHash, no JsonSchema check,
// no Entity Store fold -- exactly the per-item cost this data plane exists
// to avoid (ADR-031).
public class TelemetrySample
{
    public string ChannelId { get; set; } = default!;
    public DateTimeOffset Timestamp { get; set; }
    public long? MonotonicElapsedMicros { get; set; }
    public byte[] Value { get; set; } = default!;
    public bool LateArrivalFlag { get; set; }
}
