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
public class VitalsWorkflowDSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-vitals-workflow-d-{Guid.NewGuid():N}.db");
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
    public async Task AllVitalsWorkflowDScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        await VitalsWorkflowDScenarioAssertions.ADetectorsAlertIsCapturedNonAuthoritativelyCarryingATelemetryPointerAndStartsATrackedExpectation(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.AnAcknowledgmentWithinTheWindowSatisfiesTheTrackerAndMergesOntoTheSameEntity(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.NoAcknowledgmentByTheDeadlineEscalatesExactlyOnce(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.ALateAcknowledgmentAfterEscalationIsStillRecordedNeverRejectedAndNeverTriggersASecondEscalation(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.TheAcknowledgmentAndTheNeurologistsAuthoritativeInterpretationAreIndependentFacts(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.TheNeurologistsSignOffWithoutSufficientStepUpIsChallengedNotStored(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.TheNeurologistSignsOffAcceptedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(registry, publish, db);
        await VitalsWorkflowDScenarioAssertions.TheNeurologistSignsOffRejectedInsteadAndTheRecordNeverReachesTheAuthoritativeEntityStore(registry, publish, db);
    }
}
