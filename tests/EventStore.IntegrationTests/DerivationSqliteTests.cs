using EventStore.Derivation;
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
public class DerivationSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-derivation-{Guid.NewGuid():N}.db");
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
    public async Task AllDerivationScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var derivationRegistry = new DerivationRegistrationService(db, registry);

        await DerivationScenarioAssertions.RegisteringAValidDerivationSucceeds(registry, derivationRegistry);
        await DerivationScenarioAssertions.RegisteringWithAnUnregisteredSourceFails(registry, derivationRegistry);
        await DerivationScenarioAssertions.RegisteringWithAnOnClauseReferencingAnUndeclaredSourceFails(registry, derivationRegistry);
        await DerivationScenarioAssertions.RegisteringADerivationDefinitionCycleIsRejected(registry, derivationRegistry);
        await DerivationScenarioAssertions.FireOnceEmitsOnceAllSourcesArriveWithParentEventIdsAndHopCount(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.FireOncePendingJoinSurvivesUntilTheRemainingSourceArrives(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.ExpiredPendingJoinIsSweptWithARecordedReasonAndNeverEmits(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.ContinuousEnrichmentReEmitsOnEveryNewArrivalOnceBothSourcesHaveArrivedOnce(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.BackfillFromNowIgnoresEventsPublishedBeforeRegistration(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.HopCountExceedingMaxHopCountSkipsEmissionAndRecordsADeadLetter(registry, derivationRegistry, publish, db);
    }
}
