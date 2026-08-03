namespace EventStore.Persistence;

public sealed class SqliteEventLineageQueryProvider : IEventLineageQueryProvider
{
    public Task<IReadOnlyList<Guid>> GetAncestorEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH RECURSIVE lineage(EventId, Depth, Path) AS (
                SELECT ParentEventId, 1, ',' || CAST(ParentEventId AS TEXT) || ','
                FROM EventParents WHERE ChildEventId = @rootId
                UNION ALL
                SELECT ep.ParentEventId, l.Depth + 1, l.Path || CAST(ep.ParentEventId AS TEXT) || ','
                FROM EventParents ep JOIN lineage l ON ep.ChildEventId = l.EventId
                WHERE l.Path NOT LIKE '%,' || CAST(ep.ParentEventId AS TEXT) || ',%' AND l.Depth < 1000
            )
            SELECT DISTINCT EventId FROM lineage
            """, rootEventId, ct);

    public Task<IReadOnlyList<Guid>> GetDescendantEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH RECURSIVE lineage(EventId, Depth, Path) AS (
                SELECT ChildEventId, 1, ',' || CAST(ChildEventId AS TEXT) || ','
                FROM EventParents WHERE ParentEventId = @rootId
                UNION ALL
                SELECT ep.ChildEventId, l.Depth + 1, l.Path || CAST(ep.ChildEventId AS TEXT) || ','
                FROM EventParents ep JOIN lineage l ON ep.ParentEventId = l.EventId
                WHERE l.Path NOT LIKE '%,' || CAST(ep.ChildEventId AS TEXT) || ',%' AND l.Depth < 1000
            )
            SELECT DISTINCT EventId FROM lineage
            """, rootEventId, ct);
}
