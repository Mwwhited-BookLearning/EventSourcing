using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using EventStore.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// [DoNotParallelize] -- isolates this class's tests from every other test
// in the run, not just from each other. MSTest's own method-level
// parallelism (MSTestSettings.cs) was starting many MsSqlContainers
// concurrently, causing real, repeatable Testcontainers readiness-check
// failures under the resulting resource contention (TODO.md's "SQL
// Server Testcontainers resource-exhaustion test flakiness" -- a
// baseline run failed 15 of 24 SqlServer classes before this fix).
[DoNotParallelize]
[TestClass]
public class WebhookSqlServerTests
{
    private static MsSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _container.DisposeAsync();
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlServer(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        return new EventStoreContext(options, new SqlServerJsonPathTranslator());
    }

    [TestMethod]
    public async Task AllWebhookScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        await WebhookScenarioAssertions.RegisteringASubscriptionFreezesItsClaimSnapshotOnce(db, subscriptions);
        await WebhookScenarioAssertions.AMatchingEventIsMaskedAndEnqueuedIntoTheDurableOutbox(db, registry, publish, subscriptions, upcastChain, payloadMasker);
        await WebhookScenarioAssertions.ANonMatchingEventTypeIsNeverEnqueuedForThatSubscription(db, registry, publish, subscriptions, upcastChain, payloadMasker);
    }
}
