using EventStore.Domain.EventLog;
using EventStore.Domain.Replication;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Replication;

// ADR-033 -- "sync itself performs no routing, schema validation, or
// projection... lands in the receiving site's event log exactly as if it
// arrived from its own client Inbox." Appends directly via EventAppender
// (bypassing PublishService's claims/parent checks -- the original event
// already passed those once, at its own origin site, the same reasoning
// ADR-027's UpcastMaterializer already established for an internally-
// generated, already-validated write). Factored out of the HTTP endpoint,
// the same testable-static-method shape RouterWorker/ChannelDerivationWorker
// already establish, so this can be exercised directly without a live
// HTTP round trip.
public static class PeerSyncReceiver
{
    public static async Task<PeerSyncPushResponse> ReceiveAsync(
        EventStoreContext db, PeerSyncPushRequest request, PeerAddressBook addressBook, CancellationToken ct = default)
    {
        var maxSequenceNumberSeen = 0L;
        foreach (var payload in request.Events.OrderBy(e => e.SequenceNumber))
        {
            maxSequenceNumberSeen = Math.Max(maxSequenceNumberSeen, payload.SequenceNumber);

            // ADR-011's idempotency-by-EventId, reused for peer sync too --
            // gossip is inherently redundant (multiple peers may relay the
            // same event, and a retried tick may re-push an already-acked range).
            if (await db.Events.AsNoTracking().AnyAsync(e => e.EventId == payload.EventId, ct))
                continue;

            var storedEvent = new StoredEvent
            {
                EventId = payload.EventId,
                AppId = payload.AppId,
                EntityId = "", // resolved by this site's own local Router (ADR-021/033)
                EventType = payload.EventType,
                SchemaVersion = payload.SchemaVersion,
                ExpectedVersion = payload.ExpectedVersion,
                Payload = payload.Payload,
                PayloadHash = payload.PayloadHash,
                ChainHash = "", // computed by EventAppender, this site's own chain
                Status = "received", // this site's own local Router picks it up normally
                OccurredAt = payload.OccurredAt,
                ActorId = payload.ActorId,
                OriginId = payload.OriginId, // preserved verbatim -- which site ORIGINALLY created this fact, never overwritten by whoever's relaying it
            };

            await EventAppender.AppendAsync(db, storedEvent, payload.ParentEventIds ?? [], payload.LogicalClock, ct);
        }

        var cursor = await db.PeerSyncCursors.SingleOrDefaultAsync(c => c.PeerId == request.FromPeerId, ct);
        if (cursor is null)
        {
            cursor = new PeerSyncCursor { PeerId = request.FromPeerId };
            db.PeerSyncCursors.Add(cursor);
        }
        if (maxSequenceNumberSeen > cursor.LastReceivedSequenceNumber)
            cursor.LastReceivedSequenceNumber = maxSequenceNumberSeen;
        await db.SaveChangesAsync(ct);

        addressBook.Merge(request.KnownPeers);

        return new PeerSyncPushResponse(maxSequenceNumberSeen, addressBook.KnownPeers().ToList());
    }
}
