using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using EventStore.Domain.EventLog;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Follow.Api;

// One continuous poll loop drives both mode=tail (default) and mode=replay
// (ADR-010) -- only lastSeen's initial value differs at the call site
// (docs/06-solution-structure.md, "Follow: tail vs replay cursor").
public class EventTailReader(EventStoreContext db)
{
    public async IAsyncEnumerable<StoredEvent> TailAsync(
        string eventTypeName,
        Expression<Func<StoredEvent, bool>> predicate,
        long lastSeen,
        TimeSpan pollInterval,
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
                yield return storedEvent;
                lastSeen = storedEvent.SequenceNumber;
            }

            if (matching.Count == 0)
                await Task.Delay(pollInterval, ct);
        }
    }
}
