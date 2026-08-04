namespace EventStore.Follow.Api;

// $filter/mode/fromSequenceNumber read from the request body (ADR-012 -- QUERY,
// not GET, since $filter can carry PII/PHI in its arguments). AsOfSchemaVersion
// is ADR-028's read-time-only downcast request -- the client's own historical
// (older) schema shape, not the type's active version; GraphQL doesn't exist
// at this build stage, so this is Follow's own SSE-surface translation of the
// doc's literal transport, the same translation every prior item has made.
public record FollowRequest(string AppId, string? Filter, string? Mode, long? FromSequenceNumber, int? AsOfSchemaVersion = null);

public enum FollowMode { Tail, Replay }
