namespace EventStore.Domain.EventLog;

// Soft references, deliberately no FK constraint on either side (ADR-005):
// under Permissive mode, ParentEventId may not resolve to any StoredEvent yet.
public class EventParent
{
    public Guid ChildEventId { get; set; }
    public Guid ParentEventId { get; set; }
}
