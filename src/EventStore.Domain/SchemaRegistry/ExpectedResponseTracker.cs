namespace EventStore.Domain.SchemaRegistry;

// ADR-094 -- the durable, per-request checkpoint ExpectedResponseWatcher
// maintains, the same "durable tracker, not an in-memory timer" discipline
// ProjectionCheckpoint/PeerSyncCursor/WebhookDeliveryCursor already
// establish for their own background workers.
public class ExpectedResponseTracker
{
    public Guid RequestEventId { get; set; } // PK
    public string RequestEventType { get; set; } = default!;
    public string ExpectedResponseEventType { get; set; } = default!;
    public DateTimeOffset DeadlineAt { get; set; } // the request event's own receipt (AppendedAt) time + ExpectedResponse.Within
    public Guid? SatisfiedByEventId { get; set; } // set once a matching RespondsToEventId is observed, on time or late -- never treated as an error
    public DateTimeOffset? SatisfiedAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; } // set once ExpectedResponseMissing has been published for this row -- fires exactly once
}
