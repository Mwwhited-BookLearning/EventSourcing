using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Follow.Api;

// One continuous poll loop drives both mode=tail (default) and mode=replay
// (ADR-010) -- only lastSeen's initial value differs at the call site
// (docs/06-solution-structure.md, "Follow: tail vs replay cursor").
public class EventTailReader(EventStoreContext db, SchemaRegistryService schemaRegistry)
{
    public async IAsyncEnumerable<FollowedEvent> TailAsync(
        string eventTypeName,
        Expression<Func<StoredEvent, bool>> predicate,
        long lastSeen,
        TimeSpan pollInterval,
        ClaimsPrincipal user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var matching = await db.Events
                .AsNoTracking()
                .Where(e => e.EventType == eventTypeName && e.SequenceNumber > lastSeen)
                .Where(predicate)
                .OrderBy(e => e.SequenceNumber)
                .ToListAsync(ct);

            foreach (var storedEvent in matching)
            {
                var visibleParentIds = await GetVisibleParentEventIdsAsync(storedEvent.EventId, user, ct);
                yield return new FollowedEvent(storedEvent, visibleParentIds);
                lastSeen = storedEvent.SequenceNumber;
            }

            if (matching.Count == 0)
                await Task.Delay(pollInterval, ct);
        }
    }

    // ADR-008 -- a restricted parent's ID is omitted from the envelope without
    // blocking the event itself from streaming. A dangling (Permissive-mode,
    // never-resolved) parent reference has no EventType to check against, so
    // it's included unchanged -- "restricted" and "unresolved" are distinct
    // concepts, same distinction Lineage's own resolved/restricted flags make.
    private async Task<IReadOnlyList<Guid>> GetVisibleParentEventIdsAsync(Guid eventId, ClaimsPrincipal user, CancellationToken ct)
    {
        var parentIds = await db.EventParents
            .AsNoTracking()
            .Where(p => p.ChildEventId == eventId)
            .Select(p => p.ParentEventId)
            .ToListAsync(ct);
        if (parentIds.Count == 0)
            return [];

        var resolvedParents = await db.Events
            .AsNoTracking()
            .Where(e => parentIds.Contains(e.EventId))
            .Select(e => new { e.EventId, e.EventType })
            .ToListAsync(ct);
        var eventTypeById = resolvedParents.ToDictionary(e => e.EventId, e => e.EventType);

        var claimsByEventType = await schemaRegistry.GetActiveClaimsByNamesAsync(
            resolvedParents.Select(e => e.EventType).Distinct().ToList(), ct);

        return parentIds.Where(id =>
            !eventTypeById.TryGetValue(id, out var eventType) ||
            !claimsByEventType.TryGetValue(eventType, out var claims) ||
            RequiredClaimEvaluator.HasAny(claims, ClaimDirection.Read, user)
        ).ToList();
    }
}

public record FollowedEvent(StoredEvent Event, IReadOnlyList<Guid> VisibleParentEventIds);
