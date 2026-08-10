using EventStore.FeatureFlags;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class FeatureFlagSqlServerTests
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

    private static SchemaRegistryService CreateRegistry(EventStoreContext db) =>
        new(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    [TestMethod]
    public async Task AllFeatureFlagScenarios()
    {
        using var db = CreateContext();
        var registry = CreateRegistry(db);
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
        var featureFlags = new FeatureFlagService(db, registry, publish);
        var lineage = new LineageService(db, new SqlServerEventLineageQueryProvider(), registry);

        await FeatureFlagScenarioAssertions.SettingAFlagPublishesAHashChainedEventAndFoldsFeatureFlagStateSynchronously(db, featureFlags, lineage);
        await FeatureFlagScenarioAssertions.TwoAppIdsHoldIndependentValuesForTheSameFlagKey(db, featureFlags);
        await FeatureFlagScenarioAssertions.SettingAnExistingFlagAgainOverwritesItsValueAndAdvancesTheWatermark(db, featureFlags);
    }
}
