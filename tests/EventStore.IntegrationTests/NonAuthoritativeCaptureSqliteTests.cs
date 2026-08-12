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
public class NonAuthoritativeCaptureSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-authority-{Guid.NewGuid():N}.db");
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
    public async Task AllNonAuthoritativeCaptureScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        await NonAuthoritativeCaptureScenarioAssertions.PublishingAnEventWithAttestedClaimsPersistsAsUnattestedNeverBlockingIngestion(registry, publish);
        await NonAuthoritativeCaptureScenarioAssertions.AnEventWithAnExplicitReviewPendingMarkerPersistsAsPendingReview(registry, publish);
        await NonAuthoritativeCaptureScenarioAssertions.AnUnattestedEventReachesTheLiveViewImmediatelyButNotTheAuthoritativeEntityStore(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.OnceAcceptedTheAuthoritativeEntityStoreCatchesUpToWhatTheLiveViewAlreadyShowed(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.AuthorityStatusIsIndependentOfSchemaStatus(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.AnAuthorityDecisionRejectedEventOnAnAnnotateTypeEventNeverMutatesPayloadAndRebuildsTheEntityStore(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.RejectingTheMostRecentOfTwoAcceptedReadingsRebuildsBackToTheEarlierOnesData(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.AnAuthorityDecisionRejectedEventOnACompensateTypeEventTriggersACompensatingPatch(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.AuthorityDecisionRefDenormalizesBackToTheDecidingEvent(registry, publish, db);
        await NonAuthoritativeCaptureScenarioAssertions.TwoServersIndependentlyDisagreeingAboutReviewStatusResolvesViaConflictFlag(registry, publish, db);
    }
}
