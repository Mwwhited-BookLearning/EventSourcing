using EventStore.Domain.EventLog;
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

// Regression tests for the load-run fixes that were only ever proven by a live AppHost run:
// the throttled, paged ADR-027 Trigger 2 reconcile and the paged chain verify
// (docs/bugs/framework/database/events-table-missing-worker-indexes.md,
//  docs/bugs/framework/service/chain-verify-unbounded-in-clause.md).
[TestClass]
public class LoadRunRegressionSqliteTests
{
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-loadreg-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        var created = new EventStoreContext(options, new SqliteJsonPathTranslator());
        created.Database.Migrate();
        return created;
    }

    private static Task RegisterAsync(SchemaRegistryService registry, string appId, bool v2) =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: v2
                ? """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Status": { "type": "string" } }, "required": ["OrderId", "Status"] }"""
                : """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: v2 ? "event.OrderId as OrderId, 'Unknown' as Status" : null,
            DowncastToPrevious: v2 ? "OrderId" : null));

    private static async Task PublishManyAsync(PublishService publish, string appId, int count)
    {
        for (var i = 0; i < count; i++)
            Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, $$"""{ "OrderId": "o-{{i}}" }""", null, null), TestClaimsPrincipal.None));
    }

    [TestMethod]
    public async Task TheBacklogReconcileIsSkippedWhenNotDueAndCatchesUpWhenDue()
    {
        const string appId = "loadreg-throttle";
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        await RegisterAsync(registry, appId, v2: false);
        await PublishManyAsync(publish, appId, 3);
        await RouterWorker.RunTickAsync(db, registry, upcastChain); // folds against v1; no v2 yet
        await RegisterAsync(registry, appId, v2: true);

        await RouterWorker.RunTickAsync(db, registry, upcastChain, reconcileBacklog: false);
        Assert.AreEqual(0, await db.Events.CountAsync(e => e.AppId == appId && e.EventKind == EventKind.UpcastMaterialization), "a tick that is not reconcile-due must not scan the backlog");

        await RouterWorker.RunTickAsync(db, registry, upcastChain, reconcileBacklog: true);
        Assert.AreEqual(3, await db.Events.CountAsync(e => e.AppId == appId && e.EventKind == EventKind.UpcastMaterialization));
    }

    [TestMethod]
    public async Task AReconcileBacklogLargerThanOnePageIsFullyMaterializedExactlyOnce()
    {
        const string appId = "loadreg-pages";
        const int total = 450; // more than two reconcile pages of 200
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        await RegisterAsync(registry, appId, v2: false);
        await PublishManyAsync(publish, appId, total);
        while ((await RouterWorker.RunTickAsync(db, registry, upcastChain)).Processed > 0) { }
        await RegisterAsync(registry, appId, v2: true);

        await RouterWorker.RunTickAsync(db, registry, upcastChain);
        await RouterWorker.RunTickAsync(db, registry, upcastChain); // a second reconcile must find nothing left to do

        Assert.AreEqual(total, await db.Events.CountAsync(e => e.AppId == appId && e.EventKind == EventKind.UpcastMaterialization));
        Assert.AreEqual(total, await db.Events.Where(e => e.AppId == appId && e.EventKind == EventKind.UpcastMaterialization).Select(e => e.MaterializationOfEventId).Distinct().CountAsync(), "no original may be materialized twice");
    }

    [TestMethod]
    public async Task ChainVerifyCoversALogLargerThanOneVerifyPage()
    {
        const string appId = "loadreg-verify";
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        await RegisterAsync(registry, appId, v2: false);
        await PublishManyAsync(publish, appId, 1100); // ChainVerificationService.PageSize is 1000

        var result = await new ChainVerificationService(db).VerifyAsync(long.MaxValue);

        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(result, "a log spanning two verify pages must verify clean");
    }
}
