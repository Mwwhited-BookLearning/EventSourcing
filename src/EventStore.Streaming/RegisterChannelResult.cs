namespace EventStore.Streaming;

public abstract record RegisterChannelResult
{
    public sealed record Success : RegisterChannelResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : RegisterChannelResult;

    private RegisterChannelResult() { }
}
