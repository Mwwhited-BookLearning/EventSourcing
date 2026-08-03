using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class SchemaRegistryPostgresTests
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

    private static SchemaRegistryService CreateService(EventStoreContext db) =>
        new(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()));

    [TestMethod]
    public async Task AllSchemaRegistryScenarios()
    {
        using var db = CreateContext();
        var service = CreateService(db);

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
        command.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @name";
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = indexName;
        command.Parameters.Add(param);
        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.AreEqual(1L, count, $"Expected index {indexName} to exist");
    }
}
