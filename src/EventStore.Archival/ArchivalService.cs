using System.Text;
using EventStore.Attachments;
using EventStore.Domain.AccessLog;
using EventStore.Domain.EventLog;
using EventStore.Inbox;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Archival;

// ADR-089 -- detach a verified, contiguous segment of StoredEvent (or,
// independently, AccessLogEntry) rows past ADR-056's deployment-configured
// retention window. ADR-056 owns WHEN this runs (deployment policy, not
// yet built); this owns only HOW -- every method here is a directly-
// callable, on-demand operation, the same "exercise the mechanics
// directly" testing posture this repo's own workers already establish
// for their own static entry points, not a background worker of its own.
public class ArchivalService(
    EventStoreContext db, IAttachmentContentStore contentStore,
    ChainVerificationService eventLogVerifier, AccessLogChainVerificationService accessLogVerifier)
{
    // Verify, serialize, checkpoint, THEN detach, in that order -- so a
    // crash at any point always leaves the archived bytes/checkpoint
    // durable BEFORE the only local copy is ever removed (this design's
    // own governing "never lose or corrupt data" principle, applied to
    // the archival operation itself, not just the mechanism it protects).
    public async Task<ArchiveResult> ArchiveEventLogSegmentAsync(long throughSequenceNumber, string contentProviderKey, CancellationToken ct = default)
    {
        var verification = await eventLogVerifier.VerifyAsync(throughSequenceNumber, ct);
        if (verification is ChainVerificationResult.Tampered tampered)
            return new ArchiveResult.SegmentNotVerified(tampered.FirstDivergentSequenceNumber);

        var priorCheckpoint = await db.EventLogChainCheckpoints.AsNoTracking()
            .OrderByDescending(c => c.SequenceNumberRangeEnd).FirstOrDefaultAsync(ct);
        var rangeStart = priorCheckpoint is null ? 1 : priorCheckpoint.SequenceNumberRangeEnd + 1;
        if (rangeStart > throughSequenceNumber)
            return new ArchiveResult.NothingToArchive();

        var events = await db.Events.AsNoTracking()
            .Where(e => e.SequenceNumber >= rangeStart && e.SequenceNumber <= throughSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);
        if (events.Count == 0)
            return new ArchiveResult.NothingToArchive();

        var eventIds = events.Select(e => e.EventId).ToHashSet();
        var parentsByChild = await db.EventParents.AsNoTracking()
            .Where(p => eventIds.Contains(p.ChildEventId))
            .GroupBy(p => p.ChildEventId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(p => p.ParentEventId).ToList(), ct);
        var lines = events.Select(e => new ArchivedEventLine(e, parentsByChild.TryGetValue(e.EventId, out var p) ? p : [])).ToList();
        var bytes = Encoding.UTF8.GetBytes(new ArchivedEventLogBundle(lines).ToNdjson());
        var contentProviderRef = await contentStore.StoreAsync(bytes, ct);

        var checkpoint = new ChainCheckpoint
        {
            SequenceNumberRangeStart = rangeStart,
            SequenceNumberRangeEnd = throughSequenceNumber,
            ChainHashAtRangeEnd = events[^1].ChainHash,
            ContentProviderKey = contentProviderKey,
            ContentProviderRef = contentProviderRef,
        };
        db.EventLogChainCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);

        // ADR-005 -- EventParents carries no FK constraint at all
        // ("a Permissive event type may name a ParentEventId that doesn't
        // resolve to any StoredEvent yet"), so a still-live child's own
        // reference to one of these now-archived parents is left exactly
        // as-is, a dangling reference this design already tolerates by
        // design -- only the archived events' OWN parent-link rows (as
        // the CHILD side) are removed, since that information now lives
        // in the archived bundle itself (ArchivedEventLine.ParentEventIds).
        await db.EventParents.Where(p => eventIds.Contains(p.ChildEventId)).ExecuteDeleteAsync(ct);
        await db.Events.Where(e => e.SequenceNumber >= rangeStart && e.SequenceNumber <= throughSequenceNumber).ExecuteDeleteAsync(ct);

        return new ArchiveResult.Archived(checkpoint);
    }

    public async Task<ArchiveResult> ArchiveAccessLogSegmentAsync(long throughSequenceNumber, string contentProviderKey, CancellationToken ct = default)
    {
        var verification = await accessLogVerifier.VerifyAsync(throughSequenceNumber, ct);
        if (verification is ChainVerificationResult.Tampered tampered)
            return new ArchiveResult.SegmentNotVerified(tampered.FirstDivergentSequenceNumber);

        var priorCheckpoint = await db.AccessLogChainCheckpoints.AsNoTracking()
            .OrderByDescending(c => c.SequenceNumberRangeEnd).FirstOrDefaultAsync(ct);
        var rangeStart = priorCheckpoint is null ? 1 : priorCheckpoint.SequenceNumberRangeEnd + 1;
        if (rangeStart > throughSequenceNumber)
            return new ArchiveResult.NothingToArchive();

        var entries = await db.AccessLogEntries.AsNoTracking()
            .Where(e => e.SequenceNumber >= rangeStart && e.SequenceNumber <= throughSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);
        if (entries.Count == 0)
            return new ArchiveResult.NothingToArchive();

        var bytes = Encoding.UTF8.GetBytes(new ArchivedAccessLogBundle(entries).ToNdjson());
        var contentProviderRef = await contentStore.StoreAsync(bytes, ct);

        var checkpoint = new ChainCheckpoint
        {
            SequenceNumberRangeStart = rangeStart,
            SequenceNumberRangeEnd = throughSequenceNumber,
            ChainHashAtRangeEnd = entries[^1].ChainHash,
            ContentProviderKey = contentProviderKey,
            ContentProviderRef = contentProviderRef,
        };
        db.AccessLogChainCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);

        await db.AccessLogEntries.Where(e => e.SequenceNumber >= rangeStart && e.SequenceNumber <= throughSequenceNumber).ExecuteDeleteAsync(ct);

        return new ArchiveResult.Archived(checkpoint);
    }

    // ADR-089's own "full re-verification of an archived segment stays
    // possible on demand... same verification logic already used for the
    // live chain, applied to fetched archived bytes instead of live rows."
    public async Task<ChainVerificationResult> ReVerifyEventLogSegmentAsync(ChainCheckpoint checkpoint, CancellationToken ct = default)
    {
        var bytes = await contentStore.RetrieveAsync(checkpoint.ContentProviderRef, ct);
        var bundle = ArchivedEventLogBundle.ParseNdjson(Encoding.UTF8.GetString(bytes));

        var priorCheckpoint = await db.EventLogChainCheckpoints.AsNoTracking()
            .Where(c => c.SequenceNumberRangeEnd < checkpoint.SequenceNumberRangeStart)
            .OrderByDescending(c => c.SequenceNumberRangeEnd)
            .FirstOrDefaultAsync(ct);
        var expected = priorCheckpoint?.ChainHashAtRangeEnd ?? EventChainHash.Genesis;

        foreach (var line in bundle.Lines.OrderBy(l => l.Event.SequenceNumber))
        {
            var payloadHash = EventPayloadHash.Compute(line.Event.EventType, line.Event.Payload, line.ParentEventIds);
            expected = EventChainHash.Compute(expected, payloadHash, line.Event.SequenceNumber, line.Event.Signature);
            if (expected != line.Event.ChainHash)
                return new ChainVerificationResult.Tampered(line.Event.SequenceNumber);
        }

        // Guards a truncated/incomplete blob -- every per-line check above
        // could pass on a bundle simply missing its own trailing lines,
        // never actually reaching the checkpoint's own recorded boundary.
        return expected == checkpoint.ChainHashAtRangeEnd
            ? new ChainVerificationResult.Verified(bundle.Lines.Count)
            : new ChainVerificationResult.Tampered(checkpoint.SequenceNumberRangeEnd);
    }

    public async Task<ChainVerificationResult> ReVerifyAccessLogSegmentAsync(ChainCheckpoint checkpoint, CancellationToken ct = default)
    {
        var bytes = await contentStore.RetrieveAsync(checkpoint.ContentProviderRef, ct);
        var bundle = ArchivedAccessLogBundle.ParseNdjson(Encoding.UTF8.GetString(bytes));

        var priorCheckpoint = await db.AccessLogChainCheckpoints.AsNoTracking()
            .Where(c => c.SequenceNumberRangeEnd < checkpoint.SequenceNumberRangeStart)
            .OrderByDescending(c => c.SequenceNumberRangeEnd)
            .FirstOrDefaultAsync(ct);
        var expected = priorCheckpoint?.ChainHashAtRangeEnd ?? EventChainHash.Genesis;

        foreach (var entry in bundle.Lines.OrderBy(l => l.SequenceNumber))
        {
            expected = EventChainHash.Compute(expected, AccessLogEntryHash.Compute(entry), entry.SequenceNumber);
            if (expected != entry.ChainHash)
                return new ChainVerificationResult.Tampered(entry.SequenceNumber);
        }

        return expected == checkpoint.ChainHashAtRangeEnd
            ? new ChainVerificationResult.Verified(bundle.Lines.Count)
            : new ChainVerificationResult.Tampered(checkpoint.SequenceNumberRangeEnd);
    }
}
