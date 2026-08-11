namespace EventStore.Streaming;

public abstract record IngestSamplesResult
{
    public sealed record Accepted(string ChannelId, int SamplesWritten, int LateArrivalCount) : IngestSamplesResult;

    public sealed record ChannelNotFound : IngestSamplesResult;

    public sealed record ValidationFailed(string Error) : IngestSamplesResult;

    private IngestSamplesResult() { }
}
