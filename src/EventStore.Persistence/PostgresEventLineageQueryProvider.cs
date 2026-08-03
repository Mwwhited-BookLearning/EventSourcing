namespace EventStore.Persistence;

public sealed class PostgresEventLineageQueryProvider : IEventLineageQueryProvider
{
    // The CTE's own column list (event_id/depth/path) is deliberately unquoted,
    // lowercase, and never mixed with the quoted, mixed-case "EventParents" table
    // column names below -- Postgres folds unquoted identifiers to lowercase, so
    // referencing the CTE's own columns with quoted mixed case would look for a
    // column that was never actually created that way.
    public Task<IReadOnlyList<Guid>> GetAncestorEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH RECURSIVE lineage(event_id, depth, path) AS (
                SELECT "ParentEventId", 1, ',' || CAST("ParentEventId" AS TEXT) || ','
                FROM "EventParents" WHERE "ChildEventId" = @rootId
                UNION ALL
                SELECT ep."ParentEventId", l.depth + 1, l.path || CAST(ep."ParentEventId" AS TEXT) || ','
                FROM "EventParents" ep JOIN lineage l ON ep."ChildEventId" = l.event_id
                WHERE l.path NOT LIKE '%,' || CAST(ep."ParentEventId" AS TEXT) || ',%' AND l.depth < 1000
            )
            SELECT DISTINCT event_id FROM lineage
            """, rootEventId, ct);

    public Task<IReadOnlyList<Guid>> GetDescendantEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default) =>
        RecursiveLineageQuery.ExecuteAsync(db, """
            WITH RECURSIVE lineage(event_id, depth, path) AS (
                SELECT "ChildEventId", 1, ',' || CAST("ChildEventId" AS TEXT) || ','
                FROM "EventParents" WHERE "ParentEventId" = @rootId
                UNION ALL
                SELECT ep."ChildEventId", l.depth + 1, l.path || CAST(ep."ChildEventId" AS TEXT) || ','
                FROM "EventParents" ep JOIN lineage l ON ep."ParentEventId" = l.event_id
                WHERE l.path NOT LIKE '%,' || CAST(ep."ChildEventId" AS TEXT) || ',%' AND l.depth < 1000
            )
            SELECT DISTINCT event_id FROM lineage
            """, rootEventId, ct);
}
