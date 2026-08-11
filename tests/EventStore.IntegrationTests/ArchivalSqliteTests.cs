using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class ArchivalSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-archival-{Guid.NewGuid():N}.db");
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
    public async Task AllArchivalScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var eventLogVerifier = new ChainVerificationService(db);
        var accessLogVerifier = new AccessLogChainVerificationService(db);
        var contentStore = new InMemoryAttachmentContentStore();
        var archival = new ArchivalService(db, contentStore, eventLogVerifier, accessLogVerifier);

        await ArchivalScenarioAssertions.ArchivingAVerifiedSegmentMovesItToTheContentStoreAndLeavesACorrectCheckpoint(registry, publish, db, archival);
        await ArchivalScenarioAssertions.LiveVerificationAfterAnArchivalVerifiesOnlyTheStillLivePortionStartingFromTheCheckpoint(registry, publish, db, archival, eventLogVerifier);
        await ArchivalScenarioAssertions.RetrievingAnArchivedSegmentAndReVerifyingItsOwnInternalChainConfirmsItsUnaltered(registry, publish, db, archival);
        await ArchivalScenarioAssertions.ATamperedArchivedBlobIsDetectedOnReVerification(registry, publish, db, archival, contentStore);
        await ArchivalScenarioAssertions.ArchivingASecondSegmentChainsFromThePriorCheckpointNotFromGenesis(registry, publish, db, archival, eventLogVerifier);
        await ArchivalScenarioAssertions.ArchivingAnAlreadyTamperedLiveSegmentIsRefusedAndNothingIsDetachedOrCheckpointed(registry, publish, db, archival);
        await ArchivalScenarioAssertions.ArchivingWithNothingNewSinceThePriorCheckpointIsANoOp(registry, publish, db, archival);
        await ArchivalScenarioAssertions.AccessLogArchivesAndReVerifiesIndependentlyWithItsOwnDistinctCheckpointRow(db, archival, accessLogVerifier);
        await ArchivalScenarioAssertions.ALiveChildsReferenceToAnArchivedParentSurvivesArchivalAsAToleratedDanglingReference(registry, publish, db, archival);
    }
}
