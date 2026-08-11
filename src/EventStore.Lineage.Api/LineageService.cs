using System.Security.Claims;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Lineage.Api;

public enum LineageRootCheck { NotFound, Forbidden, Ok }

public class LineageService(EventStoreContext db, IEventLineageQueryProvider lineageQueryProvider, SchemaRegistryService schemaRegistry)
{
    // ADR-008 -- the root {eventId} a Lineage call names directly must be
    // visible to the caller or the whole request is rejected 403, distinct
    // from a genuinely unknown eventId's 404.
    public async Task<LineageRootCheck> CheckRootAsync(Guid eventId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var root = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId, ct);
        if (root is null)
            return LineageRootCheck.NotFound;

        var claims = await schemaRegistry.GetActiveClaimsByNameAsync(root.EventType, ct);
        return RequiredClaimEvaluator.HasAny(claims, ClaimDirection.Read, user) ? LineageRootCheck.Ok : LineageRootCheck.Forbidden;
    }

    public async Task<IReadOnlyList<LineageNode>> GetParentsAsync(Guid eventId, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct = default)
    {
        var parentIds = await db.EventParents.AsNoTracking()
            .Where(p => p.ChildEventId == eventId)
            .Select(p => p.ParentEventId)
            .ToListAsync(ct);
        return await ResolveNodesAsync(parentIds, user, top, skip, ct);
    }

    public async Task<IReadOnlyList<LineageNode>> GetChildrenAsync(Guid eventId, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct = default)
    {
        var childIds = await db.EventParents.AsNoTracking()
            .Where(p => p.ParentEventId == eventId)
            .Select(p => p.ChildEventId)
            .ToListAsync(ct);
        return await ResolveNodesAsync(childIds, user, top, skip, ct);
    }

    public async Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(Guid eventId, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct = default) =>
        await ResolveVisibleClosureAsync(eventId, walkUpwards: true, user, top, skip, ct);

    public async Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(Guid eventId, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct = default) =>
        await ResolveVisibleClosureAsync(eventId, walkUpwards: false, user, top, skip, ct);

    private async Task<IReadOnlyList<LineageNode>> ResolveNodesAsync(IReadOnlyList<Guid> ids, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct)
    {
        var resolvedEvents = await db.Events.AsNoTracking()
            .Where(e => ids.Contains(e.EventId))
            .ToListAsync(ct);
        var lookup = resolvedEvents.ToDictionary(e => e.EventId);
        var claimsByType = await schemaRegistry.GetActiveClaimsByNamesAsync(resolvedEvents.Select(e => e.EventType).Distinct().ToList(), ct);

        IEnumerable<LineageNode> nodes = ids.Select(id => BuildNode(id, lookup, claimsByType, user));

        if (skip is { } s) nodes = nodes.Skip(s);
        if (top is { } t) nodes = nodes.Take(t);
        return nodes.ToList();
    }

    // Ancestors/descendants: ADR-008 requires traversal to stop expanding
    // *during* recursion at a restricted node, not merely redact fields in
    // the final output -- a purely relational recursive CTE (item 4's
    // IEventLineageQueryProvider, unchanged) has no visibility concept to
    // enforce that itself. Instead: reuse that CTE's already cycle-safe,
    // depth-capped full reachable-ID set as a closure, fetch just the edges
    // *among* that already-finite set in one query, then walk the induced
    // subgraph in memory breadth-first from the root, only enqueuing a
    // node's own children/parents once that node itself is confirmed
    // visible. No SQL changes needed in any of the 3 providers.
    private async Task<IReadOnlyList<LineageNode>> ResolveVisibleClosureAsync(
        Guid rootEventId, bool walkUpwards, ClaimsPrincipal user, int? top, int? skip, CancellationToken ct)
    {
        var reachableIds = walkUpwards
            ? await lineageQueryProvider.GetAncestorEventIdsAsync(db, rootEventId, ct)
            : await lineageQueryProvider.GetDescendantEventIdsAsync(db, rootEventId, ct);
        if (reachableIds.Count == 0)
            return [];

        var closureIds = new HashSet<Guid>(reachableIds) { rootEventId };

        // walkUpwards (ancestors): edge "from" is the child, "to" is the parent.
        // Otherwise (descendants): edge "from" is the parent, "to" is the child.
        var edges = walkUpwards
            ? await db.EventParents.AsNoTracking()
                .Where(p => closureIds.Contains(p.ChildEventId) && closureIds.Contains(p.ParentEventId))
                .Select(p => new { From = p.ChildEventId, To = p.ParentEventId })
                .ToListAsync(ct)
            : await db.EventParents.AsNoTracking()
                .Where(p => closureIds.Contains(p.ParentEventId) && closureIds.Contains(p.ChildEventId))
                .Select(p => new { From = p.ParentEventId, To = p.ChildEventId })
                .ToListAsync(ct);
        var neighborsByFrom = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());

        var events = await db.Events.AsNoTracking().Where(e => closureIds.Contains(e.EventId)).ToListAsync(ct);
        var eventById = events.ToDictionary(e => e.EventId);
        var claimsByType = await schemaRegistry.GetActiveClaimsByNamesAsync(events.Select(e => e.EventType).Distinct().ToList(), ct);

        var visited = new HashSet<Guid> { rootEventId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootEventId);
        var results = new List<LineageNode>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!neighborsByFrom.TryGetValue(current, out var neighborIds))
                continue;

            foreach (var neighborId in neighborIds)
            {
                if (!visited.Add(neighborId))
                    continue;

                var node = BuildNode(neighborId, eventById, claimsByType, user);
                results.Add(node);

                // Dangling (unresolved) nodes have no further closure edges by
                // construction; a restricted node's own further neighbors are
                // deliberately never enqueued -- that's the "don't recurse
                // past it" requirement itself.
                if (node is { Resolved: true, Restricted: false })
                    queue.Enqueue(neighborId);
            }
        }

        IEnumerable<LineageNode> paged = results;
        if (skip is { } s) paged = paged.Skip(s);
        if (top is { } t) paged = paged.Take(t);
        return paged.ToList();
    }

    private static LineageNode BuildNode(
        Guid id,
        IReadOnlyDictionary<Guid, StoredEvent> eventById,
        IReadOnlyDictionary<string, IReadOnlyList<RequiredClaim>> claimsByType,
        ClaimsPrincipal user)
    {
        if (!eventById.TryGetValue(id, out var ev))
            return new LineageNode(id, null, null, null, Resolved: false);

        if (claimsByType.TryGetValue(ev.EventType, out var claims) && !RequiredClaimEvaluator.HasAny(claims, ClaimDirection.Read, user))
            return new LineageNode(id, null, null, null, Resolved: true, Restricted: true);

        return new LineageNode(ev.EventId, ev.EventType, ev.SequenceNumber, ev.OccurredAt, Resolved: true);
    }
}
