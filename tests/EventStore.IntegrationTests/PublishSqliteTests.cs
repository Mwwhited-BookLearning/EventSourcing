using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class PublishSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-publish-{Guid.NewGuid():N}.db");
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
    public async Task AllPublishScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
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
        await OpenApiScenarioAssertions.ARegisteredPublishDirectionClaimAppearsAsAnXRequiredClaimsExtension(registry, specBuilder);
        await OpenApiScenarioAssertions.PublishPayloadIsDescribedAsAStringNotANestedObject(registry, specBuilder);
    }
}
