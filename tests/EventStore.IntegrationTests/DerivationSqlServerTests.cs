using EventStore.Derivation;
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
public class DerivationSqlServerTests
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
    public async Task AllDerivationScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector(), UpcastingTestSupport.CreateChain());
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
