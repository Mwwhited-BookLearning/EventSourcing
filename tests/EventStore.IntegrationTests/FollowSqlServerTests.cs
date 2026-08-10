using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class FollowSqlServerTests
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
    public async Task AllFollowScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
        var (payloadMasker, _) = MaskingTestSupport.CreatePayloadMasker(db, registry);
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, UpcastingTestSupport.CreateChain(), UpcastingTestSupport.CreateDowncastChain()));
        var specBuilder = new AsyncApiDocumentBuilder(db, new EventSchemaConverter(), new MaskingSchemaTransformer(), cache);

        await FollowScenarioAssertions.ConnectingWithNoFilterInReplayModeStreamsEveryEventOfTheType(registry, publish, follow);
        await FollowScenarioAssertions.FilterOnANumberFieldStreamsOnlyMatchingEventsIncludingCombinedConditions(registry, publish, follow);
        await FollowScenarioAssertions.FilterReferencingAnUndeclaredFieldIsRejectedAtParseTimeBeforeAnySqlRuns(registry, publish, follow);
        await FollowScenarioAssertions.ModeReplayWithNoFromSequenceNumberDeliversHistoryThenTailsNewEventsWithNoGapOrDuplicate(registry, publish, follow);
        await FollowScenarioAssertions.SupplyingFromSequenceNumberOnlyReplaysEventsAfterThatSequenceNumber(registry, publish, follow);
        await FollowScenarioAssertions.ModeReplayCombinedWithFilterReplaysOnlyMatchingHistory(registry, publish, follow);
        await FollowScenarioAssertions.TheDefaultModeTailNeverDeliversPreExistingEvents(registry, publish, follow);
        await FollowScenarioAssertions.SupplyingFromSequenceNumberWithoutModeReplayIsRejected(registry, publish, follow);
        await FollowScenarioAssertions.ConnectingToAnUnregisteredEventTypeIsRejected(follow);
        await FollowScenarioAssertions.ConnectingWithoutTheRequiredReadClaimIsRejectedWith403(registry, publish, follow);
        await FollowScenarioAssertions.ARestrictedParentsIdIsOmittedFromParentEventIdsWithoutBlockingTheEventItself(registry, publish, follow);

        await UpcastingScenarioAssertions.AV1StoredEventIsPresentedUpcastedToTheActiveV2ShapeOnReplay(registry, publish, follow);
        await UpcastingScenarioAssertions.AV1StoredEventSpanningTwoVersionHopsAppliesBothInOrder(registry, publish, follow);

        await DowncastScenarioAssertions.ARequestForAGenuinelyOlderVersionReturnsTheOldShape(registry, publish, follow);
        await DowncastScenarioAssertions.AVersionWithNoDowncastToPreviousRegisteredFailsTheRequestRatherThanGuessing(registry, publish, follow);

        await AsyncApiScenarioAssertions.AsyncApiDocumentIncludesTheFollowChannelForARegisteredType(registry, specBuilder);
        await AsyncApiScenarioAssertions.RegisteringANewTypeInvalidatesTheCachedAsyncApiDocument(registry, specBuilder);
        await AsyncApiScenarioAssertions.AMaskablePropertyAppearsWrappedAsOneOfValueMaskedErasedInTheGeneratedDocument(registry, specBuilder);
    }
}
