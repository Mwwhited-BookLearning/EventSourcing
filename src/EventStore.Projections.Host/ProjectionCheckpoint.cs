namespace EventStore.Projections.Host;

// Shape per docs/09-cqrs-read-models.md, verbatim.
public class ProjectionCheckpoint
{
    public string ProjectionName { get; set; } = default!;
    public long LastSequenceNumber { get; set; }
}
