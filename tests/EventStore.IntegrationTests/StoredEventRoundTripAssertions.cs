using EventStore.Domain.EventLog;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared assertion logic for "Scaffolding & Persistence"'s own exit criterion:
// insert + read back a StoredEvent, Payload stored as portable text, identically
// on every provider. This harness stays live for every build-plan item after this
// one (docs/08-build-plan.md) -- it is not a one-time setup test.
internal static class StoredEventRoundTripAssertions
{
    public static async Task InsertAndReadBackAsync(EventStoreContext db)
    {
        var eventId = Guid.NewGuid();
        var stored = new StoredEvent
        {
            EventId = eventId,
            AppId = "demo",
            EntityId = "demo:Order:o-1",
            EventType = "orderplaced",
            SchemaVersion = 1,
            Payload = """{ "OrderId": "o-1", "Amount": 42.00 }""",
            PayloadHash = "test-payload-hash",
            ChainHash = "test-chain-hash",
            Status = "received",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "test-actor",
        };

        db.Events.Add(stored);
        await db.SaveChangesAsync();

        // A fresh query, not the tracked in-memory instance -- proves the round trip
        // actually went through the provider, not just the change tracker.
        var reloaded = await db.Events
            .AsNoTracking()
            .SingleAsync(e => e.EventId == eventId);

        Assert.AreEqual(stored.EntityId, reloaded.EntityId);
        Assert.AreEqual(stored.Payload, reloaded.Payload);
        Assert.AreEqual(stored.Status, reloaded.Status);
        Assert.IsGreaterThan(0, reloaded.SequenceNumber); // identity column assigned it
    }
}
