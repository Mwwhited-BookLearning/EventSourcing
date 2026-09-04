using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// [DoNotParallelize] -- matches every other SqlServer Testcontainers test
// class in this suite (see NonAuthoritativeCaptureSqlServerTests's own
// comment): MSTest's method-level parallelism starting many MsSqlContainers
// concurrently causes real, repeatable Testcontainers readiness-check
// failures under resource contention.
[DoNotParallelize]
[TestClass]
public class OrderRevealingRangeQuerySqlServerTests
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
    public async Task RangeQueryAgainstAnOrderRevealingIndexedFieldMatchesViaANativeSqlComparisonNeverDecryptingToCompare()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, _, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var (indexer, searchIndexKeyService, predicateEvaluator) = ErasureTestSupport.CreateSearchIndexStack(db, registry);
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector(), encryptor, indexer);

        await OrderRevealingRangeQueryScenarioAssertions.RangeQueryMatchesViaANativeSqlComparisonNeverDecryptingToCompare(
            db, registry, publish, searchIndexKeyService, predicateEvaluator);
    }
}
