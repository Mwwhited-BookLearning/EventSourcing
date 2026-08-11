using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class PublishPostgresTests
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
    public async Task AllPublishScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var verifier = new ChainVerificationService(db);
        var specBuilder = new OpenApiDocumentBuilder(db, new EventSchemaConverter(), cache);

        await PublishScenarioAssertions.PublishingAValidEventSucceeds(registry, publish);
        await PublishScenarioAssertions.PublishingAnEventMissingARequiredFieldIsPersistedNotRejected(registry, publish);
        await PublishScenarioAssertions.PublishingAnEventWithAWrongShapedFieldIsPersistedNotRejected(registry, publish);
        await PublishScenarioAssertions.PublishingAgainstAnUnregisteredEventTypeIsRejected(publish);
        await PublishScenarioAssertions.RetryingWithSameEventIdAndIdenticalContentReplaysWithNoNewWrite(registry, publish);
        await PublishScenarioAssertions.RetryingWithSameEventIdButDifferentContentIsAConflict(registry, publish);
        await PublishScenarioAssertions.PublishingWithoutEventIdGeneratesAFreshOneEachTime(registry, publish);
        await PublishScenarioAssertions.PublishingAnOriginEventHasNoParents(registry, publish);
        await PublishScenarioAssertions.PublishingAChildEventParentedOffAPriorEventSucceeds(registry, publish);
        await PublishScenarioAssertions.StrictParentValidationRejectsAnUnresolvedParent(registry, publish);
        await PublishScenarioAssertions.PermissiveParentValidationAcceptsADanglingParentReference(registry, publish);
        await PublishScenarioAssertions.PublishingAClaimGatedTypeWithoutTheClaimIsRejectedWith403AndWithItSucceeds(registry, publish);
        await PublishScenarioAssertions.PublishAndReadClaimsAreEnforcedFullyIndependentlyForTheSameType(registry, publish);

        await HashChainScenarioAssertions.PublishingEventsChainsEachEventsHashToItsPredecessor(registry, publish, verifier);
        await HashChainScenarioAssertions.CorruptingAHistoricalPayloadIsDetectedAtExactlyThatSequenceNumberWithEverythingBeforeItVerifyingClean(registry, publish, verifier, db);

        await OpenApiScenarioAssertions.OpenApiDocumentIncludesRegisteredPublishPaths(registry, specBuilder);
        await OpenApiScenarioAssertions.RegisteringANewTypeInvalidatesTheCachedDocument(registry, specBuilder);
    }
}
