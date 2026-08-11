namespace EventStore.Projections.Host;

// Shape per docs/09-cqrs-read-models.md, verbatim.
public class ProjectionSnapshot
{
    public string ProjectionName { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string SnapshotJson { get; set; } = default!;
    public long LastAppliedSequenceNumber { get; set; }
}
