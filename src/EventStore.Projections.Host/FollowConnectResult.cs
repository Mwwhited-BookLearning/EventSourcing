namespace EventStore.Projections.Host;

// docs/patterns/known-outcomes-are-not-exceptions.md's own named follow-on
// (TODO.md): mirrors EventStore.Follow.Api's own FollowResult exactly, one
// level up the HTTP boundary, on the CLIENT consuming that same endpoint.
// FollowClient.ConnectAsync (formerly TailAsync) used to call
// EnsureSuccessStatusCode() and let a well-understood, routine outcome
// (UnregisteredEventType, Forbidden) surface as a thrown HttpRequestException
// instead -- exactly the bug docs/bugs/framework/service/rbac-fold-404-
// logged-as-error-forever.md found and only partially fixed (one caller's
// own catch filter, not this root cause). Every caller now switches on this
// instead of catching an exception whose only distinguishing feature was an
// HttpStatusCode buried inside it.
public abstract record FollowConnectResult
{
    // The real, already-open SSE stream -- a caller consumes this exactly
    // as it consumed TailAsync's own returned IAsyncEnumerable before.
    public sealed record Connected(IAsyncEnumerable<FollowedEventEnvelope> Events) : FollowConnectResult;

    // A RequiredClaims Read-direction gate this caller's own token doesn't
    // satisfy (ADR-008/050) -- not a failure that should crash the whole
    // caller or its other, unrelated event-type connections (FollowClient's
    // own prior yield-break comment, preserved here as this case's meaning).
    public sealed record UnregisteredEventType : FollowConnectResult;
    public sealed record Forbidden : FollowConnectResult;

    // A genuinely unexpected outcome for this client's own hardcoded,
    // always-valid request shape (mode/fromSequenceNumber) -- a caller
    // should treat this as a real bug, not a routine branch, per the
    // pattern doc's own "reserve exceptions for what's left" guidance.
    public sealed record ValidationFailed(string Detail) : FollowConnectResult;

    private FollowConnectResult() { }
}
