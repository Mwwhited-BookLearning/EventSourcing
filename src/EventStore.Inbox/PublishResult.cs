namespace EventStore.Inbox;

// Pre-ADR-023 status codes, per docs/08-build-plan.md's "Publish API" item's
// own "Clarification": 201/409/400/404, not the later always-202 posture
// ("Entity-Centric Core Rebuild" introduces that, much later).
public abstract record PublishResult
{
    // EventType is the type actually stored -- normally the caller's own
    // (lowercased) event type, but ADR-020's EventUpcastFailed dead-letter
    // path stores a different, reserved type in the caller's place, and the
    // response must say so rather than silently claiming the caller's own
    // type was written.
    public sealed record Created(Guid EventId, long SequenceNumber, int SchemaVersion, string EventType) : PublishResult;

    public sealed record IdempotentReplay(Guid EventId, long SequenceNumber, int SchemaVersion) : PublishResult;

    public sealed record Conflict : PublishResult;

    public sealed record UnregisteredEventType : PublishResult;

    // ADR-008/050 -- caller lacks any Publish-direction RequiredClaims entry.
    public sealed record Forbidden : PublishResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : PublishResult;

    public sealed record UnresolvedParent(IReadOnlyList<Guid> MissingParentEventIds) : PublishResult;

    private PublishResult() { }
}
