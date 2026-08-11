using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class StreamingPostgresTests
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
    public async Task AllStreamingScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
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
