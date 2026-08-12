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
public class EntityPostgresTests
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
    public async Task AllEntityScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();

        await EntityScenarioAssertions.PublishingAnEventThatResolvesToABrandNewEntityIdCreatesAnEntityStoreRow(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingASecondEventForTheSameEntityIdUpdatesTheRowAndIncrementsVersion(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AFullEventsPayloadReplacesTheEntityStoreRowsDataWholesale(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.APartialEventsUnknownPropertyIsFoldedIntoExtensionsBagNotDropped(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingWithAStaleExpectedVersionSetsConflictFlagButStillPersistsAndFolds(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AnEventWithAnOlderOccurredAtArrivingAfterALogicallyNewerOneAlreadyFoldedSetsLateArrivalFlagAndDoesNotOverwrite(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AnEventThatIsBothAStaleExpectedVersionConflictAndALateArrivalSetsBothFlagsIndependently(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.TwoPatchesBasedOnTheSameVersionTouchingDifferentPropertiesBothFoldCleanlyWithNoConflict(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AnEventLateRelativeToTheWholeRowStillFoldsAPropertyItsOwnPreviousTouchNeverSaw(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingWithoutExpectedVersionAppliesUnconditionallyWithNoConflictDetection(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.ASchemaInvalidPublishPersistsWith202AndSchemaStatusInvalidAndKnownPropertiesStillFold(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingAgainstADeclaredVersionBehindTheActiveOneStillValidatesAgainstTheDeclaredVersion(registry, publish, db, upcastChain);
    }
}
