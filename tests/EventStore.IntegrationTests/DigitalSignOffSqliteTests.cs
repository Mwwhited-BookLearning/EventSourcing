using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EventStore.IntegrationTests;

[TestClass]
public class DigitalSignOffSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-signoff-{Guid.NewGuid():N}.db");
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
    public async Task AllDigitalSignOffScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var verifier = new ChainVerificationService(db);
        var (encryptor, _, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var signedAndEncryptedPublish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);

        await DigitalSignOffScenarioAssertions.RegisteringARequiredSignatureNamingNeitherAcrValuesNorMaxAgeIsRejected(registry);
        await DigitalSignOffScenarioAssertions.RegisteringARequiredSignatureWithANonPositiveMaxAgeIsRejected(registry);
        await DigitalSignOffScenarioAssertions.RegisteringARequiredSignatureWithOnlyAcrValuesOrOnlyMaxAgeBothSucceed(registry);
        await DigitalSignOffScenarioAssertions.APublishAgainstASignatureRequiredTypeFromACallerWithNoAcrClaimAtAllIsRejectedWithStepUpRequired(registry, publish, db);
        await DigitalSignOffScenarioAssertions.APublishWithTheWrongAcrValueIsRejectedEvenThoughAnAcrClaimIsPresent(registry, publish);
        await DigitalSignOffScenarioAssertions.APublishWithAnAuthTimeOlderThanMaxAgeIsRejectedWithStepUpRequired(registry, publish);
        await DigitalSignOffScenarioAssertions.APublishSatisfyingStepUpButOmittingMeaningIsRejectedAsAnIncompleteEnvelope(registry, publish, db);
        await DigitalSignOffScenarioAssertions.ASuccessfulSignedPublishPopulatesAllFourSignatureFields(registry, publish, db);
        await DigitalSignOffScenarioAssertions.AlteringAStoredSignaturesMeaningDirectlyInTheDatabaseIsDetectedByOrdinaryChainVerification(registry, publish, verifier, db);
        await DigitalSignOffScenarioAssertions.ErasingAnEntityWithASignedClassifiedEventLeavesSignerIdAndSignatureCompletelyIntact(registry, signedAndEncryptedPublish, db, erasureKeyService);
    }
}
