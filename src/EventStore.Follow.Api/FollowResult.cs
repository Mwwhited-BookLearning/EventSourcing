using EventStore.Domain.EventLog;

namespace EventStore.Follow.Api;

public abstract record FollowResult
{
    public sealed record Connected(IAsyncEnumerable<StoredEvent> Events) : FollowResult;

    public sealed record UnregisteredEventType : FollowResult;

    public sealed record ValidationFailed(string Error) : FollowResult;

    private FollowResult() { }
}
