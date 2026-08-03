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
public class LineagePostgresTests
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
        return new EventStoreContext(options);
    }

    [TestMethod]
    public async Task AllLineageScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()));
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var lineage = new LineageService(db, new PostgresEventLineageQueryProvider());

        await LineageScenarioAssertions.PublishingAnOriginEventShowsNoParents(registry, publish, lineage);
        await LineageScenarioAssertions.FetchingImmediateParentsAndChildrenReturnsExactlyThoseRelationships(registry, publish, lineage);
        await LineageScenarioAssertions.PermissiveValidationAcceptsADanglingParentReferenceShowingResolvedFalse(registry, publish, lineage);
        await LineageScenarioAssertions.AncestorTraversalTerminatesAcrossAPermissiveCycle(registry, publish, lineage);
        await LineageScenarioAssertions.MultiHopAncestorChainReturnsEveryAncestor(registry, publish, lineage);
        await LineageScenarioAssertions.FetchingLineageForAnUnknownEventIsRejected(lineage);
        await LineageScenarioAssertions.TopAndSkipCorrectlySliceAResultAndOmittingBothReturnsEverything(registry, publish, lineage);
    }
}
