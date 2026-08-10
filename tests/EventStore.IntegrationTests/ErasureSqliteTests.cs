using EventStore.Follow.Api;
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
public class ErasureSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-erasure-{Guid.NewGuid():N}.db");
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
    public async Task AllErasureScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, payloadMasker, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, upcastChain, UpcastingTestSupport.CreateDowncastChain()));

        await ErasureScenarioAssertions.AClassifiedFieldIsStoredAsCiphertextNeverThePlaintext(registry, publish, db);
        await ErasureScenarioAssertions.AClaimHolderSeesTheRealDecryptedValueAndANonHolderStillSeesMaskedUnaffectedByEncryption(registry, publish, follow, db, upcastChain);
        await ErasureScenarioAssertions.ErasingTheEntityDestroysTheKeyAndEveryFutureReadShowsErasedEvenForAClaimHolder(registry, publish, follow, db, upcastChain, erasureKeyService);
        await ErasureScenarioAssertions.ErasureNeverRewritesTheEventLogTheChainHashSurvivesUnchanged(registry, publish, db, upcastChain, erasureKeyService);
        await ErasureScenarioAssertions.AnErasureScopePointingAtADifferentEntityErasesThatEntitysKeyNotTheEventsOwnEntity(registry, publish, follow, db, upcastChain, erasureKeyService);
    }
}
