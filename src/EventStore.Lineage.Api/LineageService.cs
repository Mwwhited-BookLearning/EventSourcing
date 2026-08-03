using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Lineage.Api;

public class LineageService(EventStoreContext db, IEventLineageQueryProvider lineageQueryProvider)
{
    public Task<bool> EventExistsAsync(Guid eventId, CancellationToken ct = default) =>
        db.Events.AsNoTracking().AnyAsync(e => e.EventId == eventId, ct);

    public async Task<IReadOnlyList<LineageNode>> GetParentsAsync(Guid eventId, int? top, int? skip, CancellationToken ct = default)
    {
        var parentIds = await db.EventParents.AsNoTracking()
            .Where(p => p.ChildEventId == eventId)
            .Select(p => p.ParentEventId)
            .ToListAsync(ct);
        return await ResolveNodesAsync(parentIds, top, skip, ct);
    }

    public async Task<IReadOnlyList<LineageNode>> GetChildrenAsync(Guid eventId, int? top, int? skip, CancellationToken ct = default)
    {
        var childIds = await db.EventParents.AsNoTracking()
            .Where(p => p.ParentEventId == eventId)
            .Select(p => p.ChildEventId)
            .ToListAsync(ct);
        return await ResolveNodesAsync(childIds, top, skip, ct);
    }

    public async Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(Guid eventId, int? top, int? skip, CancellationToken ct = default)
    {
        var ids = await lineageQueryProvider.GetAncestorEventIdsAsync(db, eventId, ct);
        return await ResolveNodesAsync(ids, top, skip, ct);
    }

    public async Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(Guid eventId, int? top, int? skip, CancellationToken ct = default)
    {
        var ids = await lineageQueryProvider.GetDescendantEventIdsAsync(db, eventId, ct);
        return await ResolveNodesAsync(ids, top, skip, ct);
    }

    private async Task<IReadOnlyList<LineageNode>> ResolveNodesAsync(IReadOnlyList<Guid> ids, int? top, int? skip, CancellationToken ct)
    {
        var resolvedEvents = await db.Events.AsNoTracking()
            .Where(e => ids.Contains(e.EventId))
            .ToListAsync(ct);
        var lookup = resolvedEvents.ToDictionary(e => e.EventId);

        IEnumerable<LineageNode> nodes = ids.Select(id => lookup.TryGetValue(id, out var ev)
            ? new LineageNode(ev.EventId, ev.EventType, ev.SequenceNumber, ev.OccurredAt, Resolved: true)
            : new LineageNode(id, null, null, null, Resolved: false));

        if (skip is { } s) nodes = nodes.Skip(s);
        if (top is { } t) nodes = nodes.Take(t);
        return nodes.ToList();
    }
}
