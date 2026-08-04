using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class SchemaRegistrySqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-registry-{Guid.NewGuid():N}.db");
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

    private static SchemaRegistryService CreateService(EventStoreContext db) =>
        new(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    [TestMethod]
    public async Task AllSchemaRegistryScenarios()
    {
        using var db = CreateContext();
        var service = CreateService(db);

        // Runs first, against a freshly migrated empty database, so the first
        // FilterableField inserted deterministically gets Id = 1 -- the index
        // name this test verifies next depends on that.
        await SchemaRegistryScenarioAssertions.RegisteringANewEventTypeCreatesVersion1(service);
        await VerifyIndexExists(db, "IX_Events_demo_orderplaced_1_1");

        await SchemaRegistryScenarioAssertions.RegisteringSameNameUnderDifferentAppIdIsIndependent(service);
        await SchemaRegistryScenarioAssertions.RegisteringAnUpdatedSchemaCreatesNewVersionAndDeactivatesPrevious(service);
        await SchemaRegistryScenarioAssertions.RegisteringWithoutChangeKindIsRejected(service);
        await SchemaRegistryScenarioAssertions.RegisteringAFilterableFieldNotInSchemaIsRejected(service);
        await SchemaRegistryScenarioAssertions.RegisteringXMaskingDirectlyOnObjectTypedPropertyIsRejected(service);
        await SchemaRegistryScenarioAssertions.RegisteringAnUnsupportedMaskingStrategyIsRejected(service);
        await SchemaRegistryScenarioAssertions.RegisteringPartialRevealAndHashStrategiesSucceeds(service);
        await SchemaRegistryScenarioAssertions.RegulatoryMetadataFieldsAreOptional(service);
        await SchemaRegistryScenarioAssertions.ListingSupportsTopAndSkipPagination(service);
    }

    private static async Task VerifyIndexExists(EventStoreContext db, string indexName)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name";
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = indexName;
        command.Parameters.Add(param);
        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.AreEqual(1L, count, $"Expected index {indexName} to exist");
    }
}
