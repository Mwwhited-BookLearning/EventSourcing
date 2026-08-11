using EventStore.Follow.Api;
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
public class ErasurePostgresTests
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
    public async Task AllErasureScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, payloadMasker, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, upcastChain, UpcastingTestSupport.CreateDowncastChain()));

        await ErasureScenarioAssertions.AClassifiedFieldIsStoredAsCiphertextNeverThePlaintext(registry, publish, db);
        await ErasureScenarioAssertions.AClaimHolderSeesTheRealDecryptedValueAndANonHolderStillSeesMaskedUnaffectedByEncryption(registry, publish, follow, db, upcastChain);
        await ErasureScenarioAssertions.ErasingTheEntityDestroysTheKeyAndEveryFutureReadShowsErasedEvenForAClaimHolder(registry, publish, follow, db, upcastChain, erasureKeyService);
        await ErasureScenarioAssertions.ErasureNeverRewritesTheEventLogTheChainHashSurvivesUnchanged(registry, publish, db, upcastChain, erasureKeyService);
        await ErasureScenarioAssertions.AnErasureScopePointingAtADifferentEntityErasesThatEntitysKeyNotTheEventsOwnEntity(registry, publish, follow, db, upcastChain, erasureKeyService);
    }
}
