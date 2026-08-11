using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class StreamingSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-streaming-{Guid.NewGuid():N}.db");
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
    public async Task AllStreamingScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var channelRegistry = new ChannelRegistryService(db);
        var ingestOptions = Options.Create(new TelemetryIngestOptions());
        var writer = new TelemetrySampleWriter(db, registry, publish, ingestOptions);
        var (redactionResolver, _) = StreamingTestSupport.CreateRedactionResolver();
        var reader = new TelemetryTailReader(db, redactionResolver);
        var fragmentResolver = new MediaFragmentResolver(db);

        await StreamingScenarioAssertions.ABatchOfSamplesIngestsWithoutTouchingSchemaValidationHashChainOrEntityStoreFold(channelRegistry, writer, db);
        await StreamingScenarioAssertions.ADetectorPublishingAnEventWithATelemetryPointerRoundTripsThroughTheNormalPublishPipelineUnchanged(registry, publish, db);
        await StreamingScenarioAssertions.ACorrelatedMultiChannelDetectionCarriesOneTelemetryPointerEntryPerContributingChannel(registry, publish, db);
        await StreamingScenarioAssertions.ADeliberatelyReorderedSampleSetsLateArrivalFlagWithoutMovingTheHighWaterMark(channelRegistry, writer, db);
        await StreamingScenarioAssertions.ASlowUploadingProducerTriggersAChannelLagDetectedEvent(channelRegistry, writer, db);
        await StreamingScenarioAssertions.ASessionWithMultipleThreadIdGroupedChannelsRendersAsOneGroupedViewNotNUnrelatedOnes(channelRegistry, writer, reader, db);
        await StreamingScenarioAssertions.AFollowerLackingARedactedRangesRequiredClaimReceivesTheSubstitutionPlusTheSidebandExistenceFlag(channelRegistry, writer, reader, db);
        await StreamingScenarioAssertions.AFollowerHoldingTheRequiredClaimReceivesTheRealContentNotTheSubstitution(channelRegistry, writer, reader, db);
        await StreamingScenarioAssertions.ARedactedRangeConfiguredForPartialRevealSubstitutesAFormatPreservingPartialValue(channelRegistry, writer, reader, db);
        await StreamingScenarioAssertions.ADerivedChannelIsResampledFromItsSourceChannel(channelRegistry, writer, db);
        await StreamingScenarioAssertions.ADeepLinkTemporalFragmentResolvesToTheSameWindowAsATelemetryPointer(channelRegistry, writer, fragmentResolver, db);
    }
}
