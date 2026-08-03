namespace EventStore.Persistence;

public sealed class SqlServerEventLineageQueryProvider : IEventLineageQueryProvider
{
    // The anchor's Path expression must be cast to the exact same type SQL Server
    // would infer for the recursive part's (longer, growing) concatenation --
    // "Types don't match between the anchor and the recursive part" otherwise,
    // since a bare string literal concatenation infers a fixed, too-short NVARCHAR
    // length from the anchor alone.
    public Task<IReadOnlyList<Guid>> GetAncestorEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH lineage(EventId, Depth, Path) AS (
                SELECT ParentEventId, 1, CAST(',' + CAST(ParentEventId AS NVARCHAR(36)) + ',' AS NVARCHAR(MAX))
                FROM EventParents WHERE ChildEventId = @rootId
                UNION ALL
                SELECT ep.ParentEventId, l.Depth + 1, l.Path + CAST(ep.ParentEventId AS NVARCHAR(36)) + ','
                FROM EventParents ep JOIN lineage l ON ep.ChildEventId = l.EventId
                WHERE l.Path NOT LIKE '%,' + CAST(ep.ParentEventId AS NVARCHAR(36)) + ',%' AND l.Depth < 1000
            )
            SELECT DISTINCT EventId FROM lineage
            """, rootEventId, ct);

    public Task<IReadOnlyList<Guid>> GetDescendantEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH lineage(EventId, Depth, Path) AS (
                SELECT ChildEventId, 1, CAST(',' + CAST(ChildEventId AS NVARCHAR(36)) + ',' AS NVARCHAR(MAX))
                FROM EventParents WHERE ParentEventId = @rootId
                UNION ALL
                SELECT ep.ChildEventId, l.Depth + 1, l.Path + CAST(ep.ChildEventId AS NVARCHAR(36)) + ','
                FROM EventParents ep JOIN lineage l ON ep.ParentEventId = l.EventId
                WHERE l.Path NOT LIKE '%,' + CAST(ep.ChildEventId AS NVARCHAR(36)) + ',%' AND l.Depth < 1000
            )
            SELECT DISTINCT EventId FROM lineage
            """, rootEventId, ct);
}
