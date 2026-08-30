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
        // Npgsql's retrying execution strategy (EventStore.Host.Postgres's
        // EnableRetryOnFailure) forbids a manually-started transaction unless
        // the WHOLE retryable unit -- every Add and SaveChanges, not just
        // BeginTransaction/Commit -- runs inside CreateExecutionStrategy's own
        // delegate (EF throws InvalidOperationException otherwise, "does not
        // support user-initiated transactions"). Found only by actually
        // running a publish through the real Postgres-backed AppHost, which
        // is the only path that ever enables retry-on-failure -- every
        // SQLite/PostgreSQL/SQL Server integration test constructs its own
        // DbContext without it, so none of them could have caught this.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry re-enters this delegate from scratch, but storedEvent
            // is the SAME shared instance across every attempt (constructed
            // once by the caller, before this method ever runs) -- found, by
            // actually running 30 genuinely concurrent publishes against a
            // real Postgres container, to leave the hash chain silently
            // WRONG rather than just fail loudly on a retry: an attempt
            // whose transaction later aborted can still leave EF's change
            // tracker believing storedEvent's identity-generated
            // SequenceNumber was already assigned, so a bare re-Add() skips
            // re-generating it and ChainHash below gets computed from a
            // stale value that no longer matches the row this attempt is
            // actually about to insert. Detaching and resetting the one
            // property this method itself reads back is the fix -- every
            // other property this method writes (ChainHash/LogicalClock/
            // AppendedAt) is unconditionally overwritten before being read
            // again regardless of attempt, so nothing else needs resetting.
            db.Entry(storedEvent).State = EntityState.Detached;
            storedEvent.SequenceNumber = default;

            // A prior attempt's own freshly-`new`'d EventParent rows (built
            // fresh each attempt, right below) are a DIFFERENT defect from
            // storedEvent's own -- not stale data, but a duplicate-key
            // tracking conflict: EF still has the previous attempt's own
            // instances tracked under the same (ChildEventId, ParentEventId)
            // key, and `.Add()`-ing a second, different instance with that
            // same key throws. Detached defensively for the identical
            // "retry must start from a clean slate" reason as storedEvent
            // above, even though the test that caught storedEvent's own bug
            // published with no parents and so never exercised this path.
            foreach (var tracked in db.ChangeTracker.Entries<EventParent>().Where(e => e.Entity.ChildEventId == storedEvent.EventId).ToList())
                tracked.State = EntityState.Detached;

            db.Events.Add(storedEvent);
            foreach (var parentEventId in parentEventIds)
                db.EventParents.Add(new EventParent { ChildEventId = storedEvent.EventId, ParentEventId = parentEventId });

            // ADR-019/033 -- ChainHash needs this row's own SequenceNumber, which
            // isn't known until the insert itself assigns it (an identity column),
            // so this is necessarily a read-prior-state, insert, then compute-and-
            // update sequence, not one single insert. LogicalClock's own "read this
            // site's most recent clock, compute the next one" follows the identical
            // shape, in the same transaction.
            //
            // Postgres: Read Committed, not Serializable -- AppendSerializationLock's
            // own pg_advisory_xact_lock (acquired as the FIRST statement below)
            // already provides real mutual exclusion between concurrent appenders,
            // which is what actually prevents two of them from reading the same
            // "prior tail." Serializable's own snapshot-based guarantee is not just
            // redundant once that lock exists, it actively breaks the fix (see
            // AppendSerializationLock's own class comment for the two prior,
            // documented failed attempts this corrects). SQLite/SQL Server keep
            // Serializable, completely unchanged -- neither has ever exhibited the
            // Postgres-specific 40001 contention this exists to fix.
            var isolationLevel = AppendSerializationLock.IsPostgres(db) ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;
            await using var transaction = await db.Database.BeginTransactionAsync(isolationLevel, ct);
            try
            {
                // Must be the very first statement in the transaction -- see
                // AppendSerializationLock's own class comment for exactly why
                // that ordering, combined with Read Committed above, is what
                // makes this work where two earlier attempts didn't.
                await AppendSerializationLock.AcquireAsync(db, AppendSerializationLock.EventLogTailLockKey, ct);

                var prior = await db.Events
                    .AsNoTracking()
                    .OrderByDescending(e => e.SequenceNumber)
                    .Select(e => new { e.ChainHash, e.LogicalClock })
                    .FirstOrDefaultAsync(ct);

                // ADR-089 -- once the live tail has been archived away
                // (EventStore.Archival), the query above finds nothing even
                // though a real prior chain exists; falling straight to
                // EventChainHash.Genesis here would silently restart the chain
                // from zero, breaking every ChainHash computed from this point
                // on. Falls back to the latest EventLogChainCheckpoint's own
                // ChainHashAtRangeEnd instead -- found only by actually running
                // a publish immediately after an archival, not by reading the
                // code back; ChainVerificationService needed the identical fix
                // for the same reason.
                var priorChainHash = prior?.ChainHash;
                if (priorChainHash is null)
                    priorChainHash = await db.EventLogChainCheckpoints
                        .AsNoTracking()
                        .OrderByDescending(c => c.SequenceNumberRangeEnd)
                        .Select(c => c.ChainHashAtRangeEnd)
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
                storedEvent.ChainHash = EventChainHash.Compute(priorChainHash ?? EventChainHash.Genesis, storedEvent.PayloadHash, storedEvent.SequenceNumber, storedEvent.Signature);
                storedEvent.LogicalClock = HybridLogicalClock.Next(prior?.LogicalClock, observedRemoteClock);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
