using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Single-provider (SQLite) -- this sample proves domain CONFIGURATION of
// already triple-provider-proven core mechanisms (RequiredClaims,
// RequiredSignature, non-authoritative capture, the authorityDecision
// reactor), the same "no per-provider build split needed here" posture
// item 10's own CQRS projections sample already established.
[TestClass]
public class VitalsWorkflowASqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-vitals-workflow-a-{Guid.NewGuid():N}.db");
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
    public async Task AllVitalsWorkflowAScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        await VitalsWorkflowAScenarioAssertions.ACoordinatorScreensANewPatientAndTheRecordIsAcceptedImmediately(registry, publish, db);
        await VitalsWorkflowAScenarioAssertions.ACoordinatorCapturesInformedConsentWhichStartsNonAuthoritativePendingInvestigatorCountersignature(registry, publish, db);
        await VitalsWorkflowAScenarioAssertions.ACoordinatorCannotApproveTheirOwnConsentCapture(registry, publish, db);
        await VitalsWorkflowAScenarioAssertions.ThePIsCountersignatureWithoutSufficientStepUpAuthenticationIsChallengedNotStored(registry, publish, db);
        await VitalsWorkflowAScenarioAssertions.ThePICountersignsApprovedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(registry, publish, db);
        await VitalsWorkflowAScenarioAssertions.ThePIRejectsTheConsentCaptureAndEnrollmentStaysPendingUntilItsRecaptured(registry, publish, db);
    }
}
