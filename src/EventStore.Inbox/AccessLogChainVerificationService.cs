using EventStore.Domain.AccessLog;
using EventStore.Domain.EventLog;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Inbox;

// ADR-045 -- AccessLog's own independent hash chain, verified the same way
// ChainVerificationService already verifies the Event Log's: recompute from
// SequenceNumber 1 through throughSequenceNumber and report the first
// divergence, if any. A separate chain, never coupled to StoredEvent's own
// (ADR-045's own explicit "different append source, different reader, no
// reason to couple their tamper-evidence").
public class AccessLogChainVerificationService(EventStoreContext db)
{
    public async Task<ChainVerificationResult> VerifyAsync(long throughSequenceNumber, CancellationToken ct = default)
    {
        var entries = await db.AccessLogEntries
            .AsNoTracking()
            .Where(e => e.SequenceNumber <= throughSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);

        var expected = EventChainHash.Genesis;
        foreach (var entry in entries)
        {
            // Re-derived from this row's own fields, not read from the
            // stored ChainHash column -- a direct-database edit to any
            // field (e.g. ResourceRef) must still surface here as a
            // divergence, matching the Event Log verifier's identical
            // tamper-evidence promise.
            expected = EventChainHash.Compute(expected, AccessLogEntryHash.Compute(entry), entry.SequenceNumber);
            if (expected != entry.ChainHash)
                return new ChainVerificationResult.Tampered(entry.SequenceNumber);
        }

        return new ChainVerificationResult.Verified(entries.Count);
    }
}
