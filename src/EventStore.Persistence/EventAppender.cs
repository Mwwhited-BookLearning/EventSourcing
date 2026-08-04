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
    public static async Task AppendAsync(
        EventStoreContext db, StoredEvent storedEvent, IReadOnlyList<Guid> parentEventIds, CancellationToken ct = default)
    {
        db.Events.Add(storedEvent);
        foreach (var parentEventId in parentEventIds)
            db.EventParents.Add(new EventParent { ChildEventId = storedEvent.EventId, ParentEventId = parentEventId });

        // ADR-019 -- ChainHash needs this row's own SequenceNumber, which isn't
        // known until the insert itself assigns it (an identity column), so this
        // is necessarily a read-prior-hash, insert, then compute-and-update
        // sequence, not one single insert. Serializable isolation prevents a
        // concurrent appender's own insert from reading the same "prior tail"
        // and producing two rows that both chain off the same predecessor.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var priorChainHash = await db.Events
                .AsNoTracking()
                .OrderByDescending(e => e.SequenceNumber)
                .Select(e => e.ChainHash)
                .FirstOrDefaultAsync(ct) ?? EventChainHash.Genesis;

            await db.SaveChangesAsync(ct);

            storedEvent.ChainHash = EventChainHash.Compute(priorChainHash, storedEvent.PayloadHash, storedEvent.SequenceNumber);
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
