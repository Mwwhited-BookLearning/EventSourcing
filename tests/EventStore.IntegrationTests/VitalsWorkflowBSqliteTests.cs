using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class VitalsWorkflowBSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-vitals-workflow-b-{Guid.NewGuid():N}.db");
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
    public async Task AllVitalsWorkflowBScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var channelRegistry = new ChannelRegistryService(db);

        await VitalsWorkflowBScenarioAssertions.ACoordinatorPairsABedsideMonitorViaWebHidOnAChromiumBrowser(registry, publish, db);
        await VitalsWorkflowBScenarioAssertions.ACoordinatorPairsTheSameClassOfDeviceViaTheNativeBridgeFallbackOnFirefox(registry, publish);
        await VitalsWorkflowBScenarioAssertions.AnOriginTelemetryChannelIsProvisionedScopedToThePatientEntity(channelRegistry);
        await VitalsWorkflowBScenarioAssertions.ContinuousSamplesAreIngestedWithoutPerSampleValidationOrAnEntityStoreFold(registry, publish, channelRegistry, db);
        await VitalsWorkflowBScenarioAssertions.ADeviceLinkedAdverseEventIsCapturedNonAuthoritativelyCarryingATelemetryPointer(registry, publish, db);
        await VitalsWorkflowBScenarioAssertions.ASiteCoordinatorEnteredAdverseEventAlsoStartsPendingReviewViaAnExplicitMarker(registry, publish);
        await VitalsWorkflowBScenarioAssertions.ThePIsReviewDecisionWithoutSufficientStepUpAuthenticationIsChallengedNotStored(registry, publish, db);
        await VitalsWorkflowBScenarioAssertions.ThePISignsOffAcceptedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(registry, publish, db);
        await VitalsWorkflowBScenarioAssertions.ThePISignsOffRejectedInsteadAndTheRecordNeverReachesTheAuthoritativeEntityStore(registry, publish, db);
    }
}
