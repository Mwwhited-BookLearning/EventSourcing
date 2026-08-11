using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenario for "Data Lifecycle & Backup/Restore Classification"
// (docs/08-build-plan.md, ADR-056). This item's own exit criteria calls for
// a real restore drill: take a native backup of an authoritative store,
// restore to a fresh instance, re-run fold/rebuild, confirm rebuildable
// stores reconstruct identically. A real OS-level backup/restore round trip
// through each provider's own native tooling would only be testing that
// vendor's already-proven backup feature, not this project's own rebuild
// logic -- the actual thing worth verifying -- so the "restore" half is
// simulated directly: wipe the rebuildable Entity Store tables (exactly
// what a fresh instance restored from an authoritative-only backup would
// have) and reset every event back to "received" (what replaying the full
// Event Log against that fresh instance means), then re-run RouterWorker's
// own public RunOnceAsync -- the same entry point the live worker already
// uses, no separate rebuild-only code path to keep in sync.
internal static class DataLifecycleScenarioAssertions
{
    public static async Task WipingRebuildableStoresAndReplayingAllEventsReconstructsIdenticalEntityStoreState(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "data-lifecycle-demo-1";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Order"));
        await registry.RegisterAsync("OrderShipped", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "Carrier": { "type": "string" } }, "required": ["OrderId", "Carrier"] }
                """,
            FilterableFields: [], ChangeKind: "Partial", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Order"));

        // Deliberately batched into one tick, not one publish-then-fold cycle
        // per event: two events for the same entity (OrderPlaced +
        // OrderShipped, both o-dl-1) landing in the SAME RunOnceAsync call is
        // exactly the scenario that exposed a real RouterWorker bug while
        // writing this test -- FoldAsync/FoldLiveAsync's own row lookup never
        // saw the first event's own not-yet-saved row, so the second Add()ed
        // a duplicate and crashed at SaveChangesAsync. Fixed in RouterWorker
        // (checks DbSet.Local first); kept batched here so this scenario
        // keeps exercising that exact path rather than quietly avoiding it.
        await PublishAsync(publish, appId, "OrderPlaced", """{ "OrderId": "o-dl-1", "Amount": 10 }""");
        await PublishAsync(publish, appId, "OrderShipped", """{ "OrderId": "o-dl-1", "Carrier": "UPS" }""");
        await PublishAsync(publish, appId, "OrderPlaced", """{ "OrderId": "o-dl-2", "Amount": 20 }""");

        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var entityId1 = $"{appId}:order:o-dl-1";
        var entityId2 = $"{appId}:order:o-dl-2";
        var beforeAuthoritative1 = await SnapshotAuthoritativeAsync(db, entityId1);
        var beforeAuthoritative2 = await SnapshotAuthoritativeAsync(db, entityId2);
        var beforeLive1 = await SnapshotLiveAsync(db, entityId1);
        var beforeLive2 = await SnapshotLiveAsync(db, entityId2);

        // The "disaster + restore": a fresh instance recovered from an
        // authoritative-only backup starts with an empty rebuildable Entity
        // Store, and the full Event Log needs replaying against it.
        await db.EntityStore.Where(r => r.EntityId == entityId1 || r.EntityId == entityId2).ExecuteDeleteAsync();
        await db.LiveEntityStore.Where(r => r.EntityId == entityId1 || r.EntityId == entityId2).ExecuteDeleteAsync();
        var appEvents = await db.Events.Where(e => e.AppId == appId).ToListAsync();
        foreach (var storedEvent in appEvents)
            storedEvent.Status = "received";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        Assert.AreEqual(beforeAuthoritative1, await SnapshotAuthoritativeAsync(db, entityId1));
        Assert.AreEqual(beforeAuthoritative2, await SnapshotAuthoritativeAsync(db, entityId2));
        Assert.AreEqual(beforeLive1, await SnapshotLiveAsync(db, entityId1));
        Assert.AreEqual(beforeLive2, await SnapshotLiveAsync(db, entityId2));
    }

    private static async Task PublishAsync(PublishService publish, string appId, string typeName, string payload)
    {
        var result = await publish.PublishAsync(typeName, new PublishEventRequest(appId, 1, payload, null, null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
    }

    // Everything the rebuild is actually supposed to reproduce -- deliberately
    // excludes UpdatedAt (wall-clock, expected to differ across the drill).
    private static async Task<string> SnapshotAuthoritativeAsync(EventStoreContext db, string entityId)
    {
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == entityId);
        return string.Join('|', row.EntityType, row.ShardKey, row.Version, row.Data, row.Extensions, row.Hash,
            row.SchemaVersion, row.LastAppliedSequenceNumber, row.LastAppliedLogicalTime, row.LateArrivalFlag);
    }

    private static async Task<string> SnapshotLiveAsync(EventStoreContext db, string entityId)
    {
        var row = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == entityId);
        return string.Join('|', row.EntityType, row.Data, row.Extensions, row.AuthorityStatus, row.LastAppliedSequenceNumber);
    }
}
