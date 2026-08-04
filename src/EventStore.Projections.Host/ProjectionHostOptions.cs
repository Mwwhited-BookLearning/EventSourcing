namespace EventStore.Projections.Host;

public class ProjectionHostOptions
{
    // Which AppId's events this projection follows -- IProjection<T> itself
    // has no AppId concept (docs/09-cqrs-read-models.md's own sketch never
    // mentions one), so it's host-level configuration instead.
    public string AppId { get; set; } = default!;
}
