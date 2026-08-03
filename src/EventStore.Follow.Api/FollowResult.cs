namespace EventStore.Follow.Api;

public abstract record FollowResult
{
    public sealed record Connected(IAsyncEnumerable<FollowedEvent> Events) : FollowResult;

    public sealed record UnregisteredEventType : FollowResult;

    // ADR-008/050 -- caller lacks any Read-direction RequiredClaims entry.
    public sealed record Forbidden : FollowResult;

    public sealed record ValidationFailed(string Error) : FollowResult;

    private FollowResult() { }
}
