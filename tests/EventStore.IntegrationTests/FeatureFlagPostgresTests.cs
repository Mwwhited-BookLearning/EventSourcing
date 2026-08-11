using EventStore.FeatureFlags;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class FeatureFlagPostgresTests
{
    private static PostgreSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
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
            .UseNpgsql(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
            .Options;
        return new EventStoreContext(options, new PostgresJsonPathTranslator());
    }

    private static SchemaRegistryService CreateRegistry(EventStoreContext db) =>
        new(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    [TestMethod]
    public async Task AllFeatureFlagScenarios()
    {
        using var db = CreateContext();
        var registry = CreateRegistry(db);
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var featureFlags = new FeatureFlagService(db, registry, publish);
        var lineage = new LineageService(db, new PostgresEventLineageQueryProvider(), registry);

        await FeatureFlagScenarioAssertions.SettingAFlagPublishesAHashChainedEventAndFoldsFeatureFlagStateSynchronously(db, featureFlags, lineage);
        await FeatureFlagScenarioAssertions.TwoAppIdsHoldIndependentValuesForTheSameFlagKey(db, featureFlags);
        await FeatureFlagScenarioAssertions.SettingAnExistingFlagAgainOverwritesItsValueAndAdvancesTheWatermark(db, featureFlags);
    }
}
