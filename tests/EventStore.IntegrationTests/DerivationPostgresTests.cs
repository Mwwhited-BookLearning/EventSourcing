using EventStore.Derivation;
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
public class DerivationPostgresTests
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
    public async Task AllDerivationScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var derivationRegistry = new DerivationRegistrationService(db, registry, UpcastingTestSupport.CreateEvaluator());

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
        await DerivationScenarioAssertions.CalculatedFieldEvaluatesAnExpressionOverArrivedSources(registry, derivationRegistry, publish, db);
        await DerivationScenarioAssertions.RegisteringACalculatedFieldWithAnUncompilableExpressionFails(registry, derivationRegistry);
    }
}
