using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class EntitySqlServerTests
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
    public async Task AllEntityScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
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
