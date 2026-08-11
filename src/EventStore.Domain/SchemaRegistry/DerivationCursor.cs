namespace EventStore.Domain.SchemaRegistry;

// Per-(derivation, source) tailing checkpoint -- follows EventTailReader's
// own lastSeen-cursor model but persisted, so a worker restart resumes
// instead of re-tailing from BackfillMode's starting point every time.
public class DerivationCursor
{
    public string AppId { get; set; } = default!;
    public string DerivationName { get; set; } = default!;
    public string SourceEventType { get; set; } = default!;
    public long LastProcessedSequenceNumber { get; set; }
}
