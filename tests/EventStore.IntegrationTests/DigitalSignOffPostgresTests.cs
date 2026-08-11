using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class DigitalSignOffPostgresTests
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
    public async Task AllDigitalSignOffScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var verifier = new ChainVerificationService(db);
        var (encryptor, _, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var signedAndEncryptedPublish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector(), encryptor);

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
