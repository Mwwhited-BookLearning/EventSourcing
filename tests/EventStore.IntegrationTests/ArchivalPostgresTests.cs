using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class ArchivalPostgresTests
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

    [TestMethod]
    public async Task AllArchivalScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
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
