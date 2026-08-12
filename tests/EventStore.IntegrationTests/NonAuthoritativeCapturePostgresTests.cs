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
public class NonAuthoritativeCapturePostgresTests
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
    public async Task AllNonAuthoritativeCaptureScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());

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
