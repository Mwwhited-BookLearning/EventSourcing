using System.Data;
using EventStore.Domain.EventLog;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence;

// ADR-019 -- the shared hash-chain-aware insert, extracted from
// PublishService so EventStore.Router's UpcastMaterializer (ADR-027) can
// append a materialization through the SAME append path as any other
// event, per that ADR's own explicit text, without going through
// PublishService's claims/parent-link checks -- those gate an EXTERNAL
// caller's submission, not an internally-generated, already-validated
// reshape of an event that already passed them once, at its own publish
// time.
public static class EventAppender
{
    public static Task AppendAsync(
        EventStoreContext db, StoredEvent storedEvent, IReadOnlyList<Guid> parentEventIds, CancellationToken ct = default) =>
        AppendAsync(db, storedEvent, parentEventIds, observedRemoteClock: null, ct);

    // observedRemoteClock -- ADR-033: a peer-sync-received event's own
    // LogicalClock, stamped at its origin site, merged into this site's
    // running clock so it never falls behind a value it has now observed.
    // Absent for every ordinary, locally-originated publish.
    public static async Task AppendAsync(
        EventStoreContext db, StoredEvent storedEvent, IReadOnlyList<Guid> parentEventIds, string? observedRemoteClock, CancellationToken ct = default)
    {
        db.Events.Add(storedEvent);
        foreach (var parentEventId in parentEventIds)
            db.EventParents.Add(new EventParent { ChildEventId = storedEvent.EventId, ParentEventId = parentEventId });

        // ADR-019/033 -- ChainHash needs this row's own SequenceNumber, which
        // isn't known until the insert itself assigns it (an identity column),
        // so this is necessarily a read-prior-state, insert, then compute-and-
        // update sequence, not one single insert. LogicalClock's own "read this
        // site's most recent clock, compute the next one" follows the identical
        // shape, in the same transaction. Serializable isolation prevents a
        // concurrent appender's own insert from reading the same "prior tail"
        // and producing two rows that both chain off the same predecessor.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var prior = await db.Events
                .AsNoTracking()
                .OrderByDescending(e => e.SequenceNumber)
                .Select(e => new { e.ChainHash, e.LogicalClock })
                .FirstOrDefaultAsync(ct);

            await db.SaveChangesAsync(ct);

            // ADR-088 -- stamped exactly here, the same moment SequenceNumber
            // itself became known via the insert above, not at method entry
            // (which would include time spent inside the Serializable
            // transaction's own retry/contention window, not genuine append
            // latency) and not after the second SaveChangesAsync below (which
            // would exclude the ChainHash/LogicalClock computation this row
            // still needs before it's actually durable).
            storedEvent.AppendedAt = DateTimeOffset.UtcNow;
            storedEvent.ChainHash = EventChainHash.Compute(prior?.ChainHash ?? EventChainHash.Genesis, storedEvent.PayloadHash, storedEvent.SequenceNumber, storedEvent.Signature);
            storedEvent.LogicalClock = HybridLogicalClock.Next(prior?.LogicalClock, observedRemoteClock);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
