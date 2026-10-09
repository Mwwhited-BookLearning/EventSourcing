using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// RouterWorker used to load every "received" event into one change tracker and commit once at the end;
// after a burst a single tick took minutes (docs/bugs/framework/service/router-unbounded-tick-batch.md).
// A tick is now bounded by RouterWorker.BatchSize and committed per page, and events deferred by ADR-038's
// rollback gate (which stay "received" forever) must not starve the events behind them.
[TestClass]
public class RouterBatchingSqliteTests
{
    // One database per test: the deferred events of the second test stay "received" forever by design and
    // would keep the first test's drain loop from ever reaching zero.
    private const string EventTypeName = "BatchItem";
    private readonly List<string> _dbPaths = [];

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            TempDbFile.Delete(path);
    }

    private EventStoreContext CreateContext()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-routerbatch-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        var created = new EventStoreContext(options, new SqliteJsonPathTranslator());
        created.Database.Migrate();
        return created;
    }

    [TestMethod]
    public async Task ABacklogLargerThanOneBatchDrainsAcrossSeveralBoundedTicks()
    {
        const string appId = "router-batch-1";
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        await RegisterAsync(registry, appId, EventTypeName);

        var total = RouterWorker.BatchSize * 2 + 100;
        for (var i = 0; i < total; i++)
            Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync(EventTypeName, new PublishEventRequest(appId, 1, $$"""{ "Id": "item-{{i}}" }""", null, null), TestClaimsPrincipal.None));

        var ticks = 0;
        while (true)
        {
            var (processed, applied) = await RouterWorker.RunTickAsync(db, registry, upcastChain);
            if (processed == 0)
                break;
            ticks++;
            Assert.IsTrue(applied <= RouterWorker.BatchSize, $"tick {ticks} applied {applied}, more than the batch bound {RouterWorker.BatchSize}");
            Assert.IsTrue(ticks < 20, "backlog never drained");
        }

        Assert.AreEqual(3, ticks, "expected 2 full batches plus the remainder");
        Assert.AreEqual(0, await db.Events.CountAsync(e => e.AppId == appId && e.EventType.ToLower() == EventTypeName.ToLower() && e.Status == "received"));
        Assert.AreEqual(total, await db.Events.CountAsync(e => e.AppId == appId && e.EventType.ToLower() == EventTypeName.ToLower() && e.Status == "applied"));
    }

    [TestMethod]
    public async Task MoreDeferredEventsThanOneBatchDoNotStarveALaterRoutableEvent()
    {
        const string appId = "router-batch-2";
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        await RegisterAsync(registry, appId, EventTypeName);

        // Schema version 4 is ahead of the only registered version (1): ADR-038's rollback gate leaves these "received".
        for (var i = 0; i < RouterWorker.BatchSize + 50; i++)
            Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync(EventTypeName, new PublishEventRequest(appId, 4, $$"""{ "Id": "ahead-{{i}}" }""", null, null), TestClaimsPrincipal.None));
        Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync(EventTypeName, new PublishEventRequest(appId, 1, """{ "Id": "routable" }""", null, null), TestClaimsPrincipal.None));

        await RouterWorker.RunTickAsync(db, registry, upcastChain);

        Assert.AreEqual(1, await db.Events.CountAsync(e => e.AppId == appId && e.EventType.ToLower() == EventTypeName.ToLower() && e.Status == "applied"), "the routable event behind the deferred ones was starved");
        Assert.AreEqual(RouterWorker.BatchSize + 50, await db.Events.CountAsync(e => e.AppId == appId && e.EventType.ToLower() == EventTypeName.ToLower() && e.Status == "received"));
    }

    private static Task RegisterAsync(SchemaRegistryService registry, string appId, string eventType) =>
        registry.RegisterAsync(eventType, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Item"));
}
