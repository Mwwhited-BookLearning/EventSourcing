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
public class LineageSqlServerTests
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
    public async Task AllLineageScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
        var lineage = new LineageService(db, new SqlServerEventLineageQueryProvider(), registry);

        await LineageScenarioAssertions.PublishingAnOriginEventShowsNoParents(registry, publish, lineage);
        await LineageScenarioAssertions.FetchingImmediateParentsAndChildrenReturnsExactlyThoseRelationships(registry, publish, lineage);
        await LineageScenarioAssertions.PermissiveValidationAcceptsADanglingParentReferenceShowingResolvedFalse(registry, publish, lineage);
        await LineageScenarioAssertions.AncestorTraversalTerminatesAcrossAPermissiveCycle(registry, publish, lineage);
        await LineageScenarioAssertions.MultiHopAncestorChainReturnsEveryAncestor(registry, publish, lineage);
        await LineageScenarioAssertions.FetchingLineageForAnUnknownEventIsRejected(lineage);
        await LineageScenarioAssertions.TopAndSkipCorrectlySliceAResultAndOmittingBothReturnsEverything(registry, publish, lineage);
        await LineageScenarioAssertions.ARestrictedRootIsRejectedWith403DistinctFromAnUnknownRootsNotFound(registry, publish, lineage);
        await LineageScenarioAssertions.AncestorTraversalStopsAtARestrictedNodeInsteadOfJustRedactingItsFields(registry, publish, lineage);
        await LineageScenarioAssertions.ARestrictedSiblingNeverAffectsAnOtherwiseVisibleSibling(registry, publish, lineage);
    }
}
