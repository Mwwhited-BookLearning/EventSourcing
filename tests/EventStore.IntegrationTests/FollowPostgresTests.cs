using EventStore.Follow.Api;
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
public class FollowPostgresTests
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
    public async Task AllFollowScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache);
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var follow = new FollowService(db, new EventTailReader(db, registry));
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
