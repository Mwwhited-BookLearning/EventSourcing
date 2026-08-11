namespace EventStore.Streaming;

// ADR-052's existence-signal requirement -- RedactionAppliedFlag is
// structurally the same shape TelemetrySample.LateArrivalFlag already
// uses, set whenever this sample's Value has been substituted, so a caller
// lacking the claim always learns THAT redaction applied here, never
// relying on the substitution content alone to be self-evidently fake.
public record TelemetrySampleView(string ChannelId, DateTimeOffset Timestamp, byte[] Value, bool LateArrivalFlag, bool RedactionAppliedFlag);

public abstract record TelemetryTailResult
{
    public sealed record Connected(IAsyncEnumerable<TelemetrySampleView> Samples) : TelemetryTailResult;

    public sealed record ChannelNotFound : TelemetryTailResult;

    public sealed record Forbidden : TelemetryTailResult;

    public sealed record ValidationFailed(string Error) : TelemetryTailResult;

    private TelemetryTailResult() { }
}
