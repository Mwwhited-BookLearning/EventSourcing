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
public class VitalsWorkflowCSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-vitals-workflow-c-{Guid.NewGuid():N}.db");
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
    public async Task AllVitalsWorkflowCScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var (encryptor, payloadMasker, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, upcastChain, UpcastingTestSupport.CreateDowncastChain()));

        await VitalsWorkflowCScenarioAssertions.AWithdrawnSubjectsConsentWithdrawalIsRetainedForeverNeverItselfErased(registry, publish, db);
        await VitalsWorkflowCScenarioAssertions.ADataProtectionOfficerRequestsErasureForTheWithdrawnSubjectDestroyingTheEncryptionKey(registry, publish, db, erasureKeyService);
        await VitalsWorkflowCScenarioAssertions.AfterErasurePhiFieldsRenderErasedWhileStructuralFieldsRemainReadable(registry, publish, follow, db, erasureKeyService);
        await VitalsWorkflowCScenarioAssertions.ACallerWithoutTheErasureRequestClaimCannotDestroyAnotherSubjectsKey(registry, publish, db, erasureKeyService);
    }
}
