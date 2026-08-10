using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class ReplicationSqliteTests
{
    private static string _dbPathA = default!;
    private static string _dbPathB = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPathA = Path.Combine(Path.GetTempPath(), $"eventstore-replication-a-{Guid.NewGuid():N}.db");
        _dbPathB = Path.Combine(Path.GetTempPath(), $"eventstore-replication-b-{Guid.NewGuid():N}.db");
        using var dbA = CreateContext(_dbPathA);
        dbA.Database.Migrate();
        using var dbB = CreateContext(_dbPathB);
        dbB.Database.Migrate();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPathA))
            File.Delete(_dbPathA);
        if (File.Exists(_dbPathB))
            File.Delete(_dbPathB);
    }

    private static EventStoreContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    private static SchemaRegistryService NewRegistry(EventStoreContext db) =>
        new(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    private static PublishService NewPublishService(EventStoreContext db, SchemaRegistryService registry, string originId) =>
        new(db, registry, new SqliteUniqueConstraintViolationDetector(), originIdOptions: Options.Create(new OriginIdOptions { OriginId = originId }));

    [TestMethod]
    public async Task AllReplicationScenarios()
    {
        using var dbA = CreateContext(_dbPathA);
        using var dbB = CreateContext(_dbPathB);
        var registryA = NewRegistry(dbA);
        var registryB = NewRegistry(dbB);
        var publishA = NewPublishService(dbA, registryA, "site-a");
        var publishB = NewPublishService(dbB, registryB, "site-b");

        await ReplicationScenarioAssertions.AnEventPublishedAtOneSiteEventuallyReplicatesToItsPeerWithOriginIdPreserved(registryA, publishA, dbA, registryB, dbB);
        await ReplicationScenarioAssertions.ASlowUploadingSiteNeverLosesQueuedEventsAcrossASimulatedRestart(registryA, publishA, dbA, () => CreateContext(_dbPathA));
        await ReplicationScenarioAssertions.TwoSitesDisconnectedAndIndependentlyWrittenToConvergeWithAGenuineConflictFlagged(registryA, publishA, dbA, registryB, publishB, dbB);
        ReplicationScenarioAssertions.APeerAddressLearnedFromAnotherPeersResponseIsMergedIntoTheLocalAddressBook();
        await ReplicationScenarioAssertions.AnEntityOfAGivenEntityTypeAlwaysResolvesToTheSameShardKey(registryA, publishA, dbA);
    }
}
