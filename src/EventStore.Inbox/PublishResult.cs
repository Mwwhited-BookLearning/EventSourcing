namespace EventStore.Inbox;

// Pre-ADR-023 status codes, per docs/08-build-plan.md's "Publish API" item's
// own "Clarification": 201/409/400/404, not the later always-202 posture
// ("Entity-Centric Core Rebuild" introduces that, much later).
public abstract record PublishResult
{
    public sealed record Created(Guid EventId, long SequenceNumber, int SchemaVersion) : PublishResult;

    public sealed record IdempotentReplay(Guid EventId, long SequenceNumber, int SchemaVersion) : PublishResult;

    public sealed record Conflict : PublishResult;

    public sealed record UnregisteredEventType : PublishResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : PublishResult;

    public sealed record UnresolvedParent(IReadOnlyList<Guid> MissingParentEventIds) : PublishResult;

    private PublishResult() { }
}
