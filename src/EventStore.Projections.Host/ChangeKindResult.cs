using EventStore.Projections.Abstractions;

namespace EventStore.Projections.Host;

// Same discriminated-result treatment as FollowConnectResult, applied to
// FollowClient.GetChangeKindAsync's own GET /registry/{eventType}/change-kind
// call (SchemaRegistryEndpoints.cs) -- that endpoint only ever returns 200 or
// 404 today, but is gated by the same "events:follow" policy Follow itself
// uses, so a 403 is a real (if currently unreachable in practice) outcome
// too, modeled here rather than left to surface as a thrown exception if a
// caller's own token/config is ever misconfigured.
public abstract record ChangeKindResult
{
    public sealed record Found(ChangeKind ChangeKind) : ChangeKindResult;
    public sealed record UnregisteredEventType : ChangeKindResult;
    public sealed record Forbidden : ChangeKindResult;

    private ChangeKindResult() { }
}
