namespace EventStore.Follow.Api;

// $filter/mode/fromSequenceNumber read from the request body (ADR-012 -- QUERY,
// not GET, since $filter can carry PII/PHI in its arguments).
public record FollowRequest(string AppId, string? Filter, string? Mode, long? FromSequenceNumber);

public enum FollowMode { Tail, Replay }
