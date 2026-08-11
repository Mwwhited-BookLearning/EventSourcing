using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class ArchivalSqlServerTests
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
    public async Task AllArchivalScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
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
