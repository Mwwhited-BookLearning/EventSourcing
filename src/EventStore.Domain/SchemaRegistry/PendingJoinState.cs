namespace EventStore.Domain.SchemaRegistry;

// FireOnce-mode join state, durable and TTL-bounded rather than an
// in-memory cache (ADR-007) -- survives a worker restart and is dropped
// with a recorded reason if the remaining sources never arrive. Not used
// by ContinuousEnrichment mode, which never waits.
public class PendingJoinState
{
    public Guid Id { get; set; }
    public string AppId { get; set; } = default!;
    public string DerivationName { get; set; } = default!;
    public string JoinKeyValue { get; set; } = default!;
    public string ArrivedSourcesJson { get; set; } = default!;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ExpiredReason { get; set; }
}
