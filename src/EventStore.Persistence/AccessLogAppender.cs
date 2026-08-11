using System.Data;
using EventStore.Domain.AccessLog;
using EventStore.Domain.EventLog;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence;

// ADR-045 -- explicit composition (ADR-041), never an auto-injected aspect:
// each read endpoint/resolver calls this directly in its own composition.
// Mirrors EventAppender's own read-prior-state/insert/compute-chain/update
// shape exactly -- SequenceNumber is an identity column not known until the
// insert itself assigns it, so ChainHash necessarily follows in a second
// SaveChanges within the same Serializable transaction.
public static class AccessLogAppender
{
    public static async Task AppendAsync(
        EventStoreContext db, string readerActorId, string readerTrustBasis, Guid? grantRef,
        string viewAccessed, string resourceRef, string action, CancellationToken ct = default)
    {
        var entry = new AccessLogEntry
        {
            ReaderActorId = readerActorId,
            ReaderTrustBasis = readerTrustBasis,
            GrantRef = grantRef,
            ViewAccessed = viewAccessed,
            ResourceRef = resourceRef,
            Action = action,
            AccessedAt = DateTimeOffset.UtcNow,
            ChainHash = "", // computed below, once SequenceNumber is known
        };
        db.AccessLogEntries.Add(entry);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var prior = await db.AccessLogEntries
                .AsNoTracking()
                .OrderByDescending(e => e.SequenceNumber)
                .Select(e => e.ChainHash)
                .FirstOrDefaultAsync(ct);

            // ADR-089 -- the identical fallback EventAppender.AppendAsync
            // now needs for the Event Log's own chain: once AccessLog's own
            // live tail has been archived away, nothing is left to read a
            // "prior" ChainHash from even though a real prior chain exists.
            var priorChainHash = prior ?? await db.AccessLogChainCheckpoints
                .AsNoTracking()
                .OrderByDescending(c => c.SequenceNumberRangeEnd)
                .Select(c => c.ChainHashAtRangeEnd)
                .FirstOrDefaultAsync(ct);

            await db.SaveChangesAsync(ct);

            entry.ChainHash = EventChainHash.Compute(priorChainHash ?? EventChainHash.Genesis, AccessLogEntryHash.Compute(entry), entry.SequenceNumber);
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
