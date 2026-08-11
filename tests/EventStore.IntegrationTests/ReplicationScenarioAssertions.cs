using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Replication;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Sharding & Replication" (docs/08-build-plan.md),
// mirroring docs/features/replication-and-sharding.md's own Gherkin.
// Exercises PeerSyncReceiver/PeerSyncWorker's static methods directly
// against TWO separate EventStoreContext instances (one per simulated
// site), the same "exercise the mechanics directly" pattern RouterWorker/
// ChannelDerivationWorker's own tests already establish -- no live HTTP
// round trip needed to prove the replication mechanism itself is correct.
// Each site's own SchemaRegistryService/PublishService is constructed by
// the calling provider test class (with that provider's own DDL
// generator/violation detector), not here -- the same division of
// responsibility every other *ScenarioAssertions.cs file in this repo
// already follows.
internal static class ReplicationScenarioAssertions
{
    private static Task RegisterOrderPlaced(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task AnEventPublishedAtOneSiteEventuallyReplicatesToItsPeerWithOriginIdPreserved(
        SchemaRegistryService registryA, PublishService publishA, EventStoreContext dbA,
        SchemaRegistryService registryB, EventStoreContext dbB)
    {
        const string appId = "replication-demo-1";
        await RegisterOrderPlaced(registryA, appId);
        await RegisterOrderPlaced(registryB, appId);

        var created = (PublishResult.Accepted)await publishA.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-1", "Amount": 42.00 }""", null, null), TestClaimsPrincipal.None);

        await SyncAllAsync(dbA, dbB, "site-a");

        var replicated = await dbB.Events.AsNoTracking().SingleAsync(e => e.EventId == created.CorrelationId);
        Assert.AreEqual("site-a", replicated.OriginId);
        Assert.AreEqual("received", replicated.Status, "the receiving site's own local Router folds it, not the sender");

        var upcastChainB = UpcastingTestSupport.CreateChain();
        await RouterWorker.RunOnceAsync(dbB, registryB, upcastChainB);

        var rowB = await dbB.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:rep-1");
        Assert.AreEqual("site-a", rowB.LastAppliedOriginId);
    }

    public static async Task ASlowUploadingSiteNeverLosesQueuedEventsAcrossASimulatedRestart(
        SchemaRegistryService registryA, PublishService publishA, EventStoreContext dbA, Func<EventStoreContext> reopenSiteA)
    {
        const string appId = "replication-demo-2";
        await RegisterOrderPlaced(registryA, appId);
        var accepted = await publishA.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-2", "Amount": 1.00 }""", null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(accepted);

        // Simulate an unclean process restart: open a fresh context against
        // the same underlying database -- the append above already
        // durably committed, so there is nothing "in flight" to lose.
        await using var reopened = reopenSiteA();
        // Scoped to "orderplaced" specifically, not every event under this
        // AppId -- ADR-067's own SchemaRegistered audit event legitimately
        // also exists here, from RegisterOrderPlaced's own registration above.
        var stillPending = await reopened.Events.AsNoTracking().Where(e => e.AppId == appId && e.EventType == "orderplaced").ToListAsync();
        Assert.AreEqual(1, stillPending.Count, "the durable Events table itself IS the fault/abend/restart-tolerant outbox -- nothing queued is lost");

        // No PeerSyncCursor exists yet for "site-b" -- resuming sync after
        // restart starts from LastAckedSequenceNumber 0, the same
        // "durable checkpoint, not memory" discipline ProjectionCheckpoint
        // already established, re-sending only what was never acked.
        var cursor = await reopened.PeerSyncCursors.SingleOrDefaultAsync(c => c.PeerId == "site-b");
        Assert.IsNull(cursor, "no partial progress existed before the restart -- everything queued is still owed in full");
    }

    public static async Task TwoSitesDisconnectedAndIndependentlyWrittenToConvergeWithAGenuineConflictFlagged(
        SchemaRegistryService registryA, PublishService publishA, EventStoreContext dbA,
        SchemaRegistryService registryB, PublishService publishB, EventStoreContext dbB)
    {
        const string appId = "replication-demo-3";
        await RegisterOrderPlaced(registryA, appId);
        await RegisterOrderPlaced(registryB, appId);
        var upcastChainA = UpcastingTestSupport.CreateChain();
        var upcastChainB = UpcastingTestSupport.CreateChain();

        // Establish "rep-3" at both sites, fully in sync, before disconnecting.
        await publishA.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-3", "Amount": 1.00 }""", null, null), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(dbA, registryA, upcastChainA);
        await SyncAllAsync(dbA, dbB, "site-a");
        await RouterWorker.RunOnceAsync(dbB, registryB, upcastChainB);

        // Disconnected: both sites independently patch the same entity
        // against the SAME ExpectedVersion, before syncing with each other.
        var fromA = (PublishResult.Accepted)await publishA.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-3", "Amount": 2.00 }""", null, null, ExpectedVersion: 1), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(dbA, registryA, upcastChainA);

        var fromB = (PublishResult.Accepted)await publishB.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-3", "Amount": 3.00 }""", null, null, ExpectedVersion: 1), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(dbB, registryB, upcastChainB);

        // Reconnect: sync each site's new event to the other, then let
        // each site's own local Router fold it.
        await SyncAllAsync(dbA, dbB, "site-a");
        await RouterWorker.RunOnceAsync(dbB, registryB, upcastChainB);
        await SyncAllAsync(dbB, dbA, "site-b");
        await RouterWorker.RunOnceAsync(dbA, registryA, upcastChainA);

        // Whichever of the two arrived SECOND at each site loses the
        // fold-time conflict check there -- ADR-024's ConflictFlag, reused
        // outright, no second cross-origin resolution mechanism.
        var bsCopyAtA = await dbA.Events.AsNoTracking().SingleAsync(e => e.EventId == fromB.CorrelationId);
        var asCopyAtB = await dbB.Events.AsNoTracking().SingleAsync(e => e.EventId == fromA.CorrelationId);
        Assert.IsTrue(bsCopyAtA.ConflictFlag, "Site B's patch arrives at Site A after Site A's own already won the ExpectedVersion check there");
        Assert.IsTrue(asCopyAtB.ConflictFlag, "symmetrically, Site A's patch arrives at Site B after Site B's own already won there");

        var rowA = await dbA.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:rep-3");
        var rowB = await dbB.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:rep-3");
        Assert.AreEqual(rowA.Data, rowB.Data, "both sites converge on the same final state -- ConflictFlag informs, it never blocks the write");
    }

    public static void APeerAddressLearnedFromAnotherPeersResponseIsMergedIntoTheLocalAddressBook()
    {
        // ADR-051 -- a newly-deployed peer configured with only ONE seed
        // still learns the rest of the mesh transitively: the mechanism
        // that makes that possible is this merge, exercised directly here.
        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions { SeedPeers = ["https://site-a.example"] }));
        Assert.AreEqual(1, addressBook.KnownAddresses.Count);

        addressBook.Merge([new KnownPeer("site-b", "https://site-b.example"), new KnownPeer("site-c", "https://site-c.example")]);

        Assert.AreEqual(3, addressBook.KnownAddresses.Count, "a peer's own response introduces addresses this site was never directly configured with");
        Assert.IsTrue(addressBook.KnownAddresses.Contains("https://site-b.example"));
        Assert.AreEqual("site-b", addressBook.PeerIdFor("https://site-b.example"));
    }

    // ADR-061 -- Region travels on the SAME gossip merge as PeerId, so a
    // transitively-learned peer's own residency tag propagates without this
    // site ever contacting it directly via /peer-sync/whoami first.
    public static void APeersRegionLearnedFromAnotherPeersGossipResponseIsMergedAlongsideItsPeerId()
    {
        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions()));
        Assert.IsNull(addressBook.RegionFor("https://site-d.example"), "an address this site has never heard of has no known region");

        addressBook.Merge([new KnownPeer("site-d", "https://site-d.example", "eu-west")]);

        Assert.AreEqual("site-d", addressBook.PeerIdFor("https://site-d.example"));
        Assert.AreEqual("eu-west", addressBook.RegionFor("https://site-d.example"));
    }

    public static async Task AnEntityOfAGivenEntityTypeAlwaysResolvesToTheSameShardKey(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "replication-demo-5";
        await RegisterOrderPlaced(registry, appId);
        await registry.RegisterAsync("CustomerRegistered", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "CustomerId": { "type": "string" }, "Name": { "type": "string" } }, "required": ["CustomerId", "Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.CustomerId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-5a", "Amount": 1.00 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "rep-5b", "Amount": 1.00 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("CustomerRegistered", new PublishEventRequest(appId, 1, """{ "CustomerId": "rep-5c", "Name": "A. Smith" }""", null, null), TestClaimsPrincipal.None);

        var upcastChain = UpcastingTestSupport.CreateChain();
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var order1Row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:rep-5a");
        var order2Row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:rep-5b");
        var customerRow = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:customerregistered:rep-5c");

        Assert.AreEqual("orderplaced", order1Row.ShardKey);
        Assert.AreEqual(order1Row.ShardKey, order2Row.ShardKey, "every Order entity resolves to the same shard regardless of which one");
        Assert.AreNotEqual(order1Row.ShardKey, customerRow.ShardKey, "a different EntityType resolves to a different shard");
    }

    // Pushes everything site "from" has to site "to" (PeerSyncReceiver's
    // own idempotent-by-EventId skip makes this safe to call repeatedly) --
    // the same "read everything since the cursor, push it" step a real
    // PeerSyncWorker tick performs, minus the HTTP round trip.
    private static async Task SyncAllAsync(EventStoreContext from, EventStoreContext to, string fromPeerId)
    {
        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions()));
        var events = await from.Events.AsNoTracking().OrderBy(e => e.SequenceNumber).ToListAsync();
        var request = new PeerSyncPushRequest(fromPeerId, events.Select(PeerSyncWorker.ToPayload).ToList(), []);
        await PeerSyncReceiver.ReceiveAsync(to, request, addressBook);
    }
}
