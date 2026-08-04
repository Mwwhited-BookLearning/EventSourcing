using EventStore.Follow.Api;
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
public class FollowSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-follow-{Guid.NewGuid():N}.db");
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
    public async Task AllFollowScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var (payloadMasker, _) = MaskingTestSupport.CreatePayloadMasker();
        var follow = new FollowService(db, new EventTailReader(db, registry, payloadMasker));
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

        await AsyncApiScenarioAssertions.AsyncApiDocumentIncludesTheFollowChannelForARegisteredType(registry, specBuilder);
        await AsyncApiScenarioAssertions.RegisteringANewTypeInvalidatesTheCachedAsyncApiDocument(registry, specBuilder);
        await AsyncApiScenarioAssertions.AMaskablePropertyAppearsWrappedAsOneOfValueMaskedErasedInTheGeneratedDocument(registry, specBuilder);
    }
}
