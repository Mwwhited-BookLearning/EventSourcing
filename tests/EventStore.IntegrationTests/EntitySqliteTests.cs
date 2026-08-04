using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class EntitySqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-entity-{Guid.NewGuid():N}.db");
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
    public async Task AllEntityScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();

        await EntityScenarioAssertions.PublishingAnEventThatResolvesToABrandNewEntityIdCreatesAnEntityStoreRow(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingASecondEventForTheSameEntityIdUpdatesTheRowAndIncrementsVersion(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AFullEventsPayloadReplacesTheEntityStoreRowsDataWholesale(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.APartialEventsUnknownPropertyIsFoldedIntoExtensionsBagNotDropped(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingWithAStaleExpectedVersionSetsConflictFlagButStillPersistsAndFolds(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AnEventWithAnOlderOccurredAtArrivingAfterALogicallyNewerOneAlreadyFoldedSetsLateArrivalFlagAndDoesNotOverwrite(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.AnEventThatIsBothAStaleExpectedVersionConflictAndALateArrivalSetsBothFlagsIndependently(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingWithoutExpectedVersionAppliesUnconditionallyWithNoConflictDetection(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.ASchemaInvalidPublishPersistsWith202AndSchemaStatusInvalidAndKnownPropertiesStillFold(registry, publish, db, upcastChain);
        await EntityScenarioAssertions.PublishingAgainstADeclaredVersionBehindTheActiveOneStillValidatesAgainstTheDeclaredVersion(registry, publish, db, upcastChain);
    }
}
