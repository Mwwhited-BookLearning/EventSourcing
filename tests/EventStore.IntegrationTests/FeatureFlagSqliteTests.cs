using EventStore.FeatureFlags;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class FeatureFlagSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-feature-flags-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    private static SchemaRegistryService CreateRegistry(EventStoreContext db) =>
        new(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    [TestMethod]
    public async Task AllFeatureFlagScenarios()
    {
        using var db = CreateContext();
        var registry = CreateRegistry(db);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var featureFlags = new FeatureFlagService(db, registry, publish);
        var lineage = new LineageService(db, new SqliteEventLineageQueryProvider(), registry);

        await FeatureFlagScenarioAssertions.SettingAFlagPublishesAHashChainedEventAndFoldsFeatureFlagStateSynchronously(db, featureFlags, lineage);
        await FeatureFlagScenarioAssertions.TwoAppIdsHoldIndependentValuesForTheSameFlagKey(db, featureFlags);
        await FeatureFlagScenarioAssertions.SettingAnExistingFlagAgainOverwritesItsValueAndAdvancesTheWatermark(db, featureFlags);
    }
}
