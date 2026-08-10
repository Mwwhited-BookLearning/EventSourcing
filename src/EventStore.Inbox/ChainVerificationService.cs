using EventStore.Domain.EventLog;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Inbox;

// ADR-019 -- recomputes the hash chain from SequenceNumber 1 through
// throughSequenceNumber and reports the first divergence, if any. O(n) from
// the seed by design (a linear chain, not a Merkle tree) -- cheap for a
// periodic integrity audit, not for cheaply verifying one arbitrary event's
// position in isolation.
public class ChainVerificationService(EventStoreContext db)
{
    public async Task<ChainVerificationResult> VerifyAsync(long throughSequenceNumber, CancellationToken ct = default)
    {
        var events = await db.Events
            .AsNoTracking()
            .Where(e => e.SequenceNumber <= throughSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Select(e => new { e.EventId, e.SequenceNumber, e.EventType, e.Payload, e.ChainHash, e.Signature })
            .ToListAsync(ct);

        var eventIds = events.Select(e => e.EventId).ToList();
        var parentIdsByEvent = await db.EventParents
            .AsNoTracking()
            .Where(p => eventIds.Contains(p.ChildEventId))
            .GroupBy(p => p.ChildEventId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(p => p.ParentEventId).ToList(), ct);

        var expected = EventChainHash.Genesis;
        foreach (var e in events)
        {
            // Re-derived from this row's own EventType/Payload/parent links, not
            // read from the stored PayloadHash column -- a direct-database edit
            // to Payload alone (leaving PayloadHash untouched) must still surface
            // here as a divergence, matching ADR-019's own tamper-evidence promise.
            var parentIds = parentIdsByEvent.TryGetValue(e.EventId, out var ids) ? ids : [];
            var payloadHash = EventPayloadHash.Compute(e.EventType, e.Payload, parentIds);
            // ADR-066 -- re-derived from this row's own stored Signature (not
            // just Payload), so tampering SignerId/SignedAt/Meaning/Acr
            // directly in the database surfaces here too, the same "recompute
            // from the actual row, don't trust a stored column blindly"
            // discipline the Payload/PayloadHash re-derivation above already
            // established.
            expected = EventChainHash.Compute(expected, payloadHash, e.SequenceNumber, e.Signature);
            if (expected != e.ChainHash)
                return new ChainVerificationResult.Tampered(e.SequenceNumber);
        }

        return new ChainVerificationResult.Verified(events.Count);
    }
}

public abstract record ChainVerificationResult
{
    public sealed record Verified(int EventCount) : ChainVerificationResult;

    // The first SequenceNumber where the stored and recomputed ChainHash
    // diverge -- everything strictly before it verifies clean.
    public sealed record Tampered(long FirstDivergentSequenceNumber) : ChainVerificationResult;

    private ChainVerificationResult() { }
}
