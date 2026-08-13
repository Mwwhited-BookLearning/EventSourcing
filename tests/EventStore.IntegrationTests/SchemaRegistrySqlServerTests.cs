using EventStore.Persistence;
using EventStore.SchemaRegistry;
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
public class SchemaRegistrySqlServerTests
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

    private static SchemaRegistryService CreateService(EventStoreContext db) =>
        new(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

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
        await SchemaRegistryScenarioAssertions.RegisteringAFieldDeclaringPciSadIsRejected(service);
        await SchemaRegistryScenarioAssertions.RegisteringTheOrdinaryPciClassificationForAFullCardNumberSucceedsUnaffectedByTheSadBoundary(service);
        await SchemaRegistryScenarioAssertions.ListingSupportsTopAndSkipPagination(service);
    }

    private static async Task VerifyIndexExists(EventStoreContext db, string indexName)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name = @name";
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = indexName;
        command.Parameters.Add(param);
        var count = (int)(await command.ExecuteScalarAsync())!;
        Assert.AreEqual(1, count, $"Expected index {indexName} to exist");
    }
}
