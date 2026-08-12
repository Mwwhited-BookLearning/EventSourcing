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
public class MeridianWorkflowCSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-meridian-workflow-c-{Guid.NewGuid():N}.db");
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
    public async Task AllMeridianWorkflowCScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        await MeridianWorkflowCScenarioAssertions.ARoutinePeriodicScreeningWithNoMatchIsAcceptedAndFoldsImmediately(registry, publish, db);
        await MeridianWorkflowCScenarioAssertions.ASanctionsListMatchIsAlwaysCapturedAsPendingReviewRegardlessOfConfidence(registry, publish, db);
        await MeridianWorkflowCScenarioAssertions.AUserHoldingNeitherIdentityReviewNorIdentityAmlReviewCannotDecideAFlaggedMatch(registry, publish, db);
        await MeridianWorkflowCScenarioAssertions.AComplianceOfficerHoldingIdentityAmlReviewConfirmsTheHitAndTheEntityStoreCatchesUp(registry, publish, db);
        // Runs before the two SAR-filing scenarios below, so its own
        // "no sarfilingrecorded event exists yet for this AppId" check
        // can't be confused by either of those scenarios' own later filing.
        await MeridianWorkflowCScenarioAssertions.AComplianceOfficerClearsAFlaggedMatchAsAFalsePositiveAndNoSarIsFiled(registry, publish, db);
        await MeridianWorkflowCScenarioAssertions.FilingASarWithoutSufficientStepUpFailsWithAnRfc9470Challenge(registry, publish, db);
        await MeridianWorkflowCScenarioAssertions.AfterSteppingUpTheRetriedSarFilingSucceedsAndCapturesASignature(registry, publish, db);
    }
}
