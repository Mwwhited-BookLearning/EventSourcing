namespace EventStore.Inbox;

// ADR-023's persist-everything posture -- superseding this file's own
// pre-rebuild shape (ValidationFailed/Created no longer exist; see
// docs/changes/2026-08-04.md's "Entity-Centric Core Rebuild" entry). Only
// two blocking rejections survive as real errors (both still genuinely
// "there is no event to persist against at all," not a content problem):
// an entirely unregistered event type, and a Strict-mode publish naming an
// unresolved parent. A missing/invalid token or scope/claim is still a real
// 401/403 too (ADR-023's own consequence: this posture is about *content*,
// never about whether the caller may call the endpoint at all).
public abstract record PublishResult
{
    // Every syntactically-parseable, authorized, non-conflicting publish
    // reaches this branch and returns 202 -- regardless of schema/entity
    // validity, which the async Router determines afterward and never
    // gates this response on. SchemaStatus/EntityId/ConflictFlag reflect
    // whatever is already known AT THIS SYNCHRONOUS MOMENT: null/""/false
    // for a freshly-inserted event (the Router hasn't run yet), or the
    // event's current, already-processed values for an idempotent replay
    // of a request the Router has since caught up with.
    public sealed record Accepted(
        Guid CorrelationId,
        long SequenceNumber,
        string Status,
        string EntityId,
        string? SchemaStatus,
        string AuthorityStatus,
        bool ConflictFlag,
        string? Reason) : PublishResult;

    public sealed record Conflict : PublishResult; // 409 -- eventId reused w/ different content (ADR-011, unaffected by ADR-023)

    public sealed record UnregisteredEventType : PublishResult; // 404 -- ADR-023 doesn't cover this: no schema/AppId context to persist against at all

    // ADR-008/050 -- caller lacks any Publish-direction RequiredClaims entry, or the events:publish scope itself (checked one level up, at the endpoint).
    public sealed record Forbidden : PublishResult;

    public sealed record UnresolvedParent(IReadOnlyList<Guid> MissingParentEventIds) : PublishResult; // 400 -- Strict-mode parent link, ADR-005, unaffected by ADR-023

    // ADR-066 -- 401, RFC 9470's own challenge shape (WWW-Authenticate,
    // built at the endpoint layer since that's an HTTP-response concern,
    // not this service's). A real, distinguishable rejection before
    // storage, the same category as Forbidden above -- about
    // authentication STRENGTH, never about the event's own content.
    public sealed record StepUpRequired(IReadOnlyList<string> AcrValues, int? MaxAge) : PublishResult;

    // ADR-066 -- 400: the step-up itself was satisfied, but Meaning (21 CFR
    // Part 11 §11.50's third linked element) was absent -- an incomplete
    // envelope, never persisted with an advisory flag the way an ordinary
    // unknown-schema publish would be.
    public sealed record MissingSignatureMeaning : PublishResult;

    private PublishResult() { }
}
