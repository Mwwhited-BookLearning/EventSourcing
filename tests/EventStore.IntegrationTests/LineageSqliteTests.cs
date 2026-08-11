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
public class LineageSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-lineage-{Guid.NewGuid():N}.db");
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

    [TestMethod]
    public async Task AllLineageScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var lineage = new LineageService(db, new SqliteEventLineageQueryProvider(), registry);

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
