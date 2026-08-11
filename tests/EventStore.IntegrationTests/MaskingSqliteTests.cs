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
public class MaskingSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-masking-{Guid.NewGuid():N}.db");
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
    public async Task AllMaskingScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var (payloadMasker, logs) = MaskingTestSupport.CreatePayloadMasker(db, registry);
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, UpcastingTestSupport.CreateChain(), UpcastingTestSupport.CreateDowncastChain()));

        await MaskingScenarioAssertions.AFollowerWithoutTheMaskingClaimSeesMaskedAndWithItSeesValue(registry, publish, follow);
        await MaskingScenarioAssertions.ALogCallTouchingAClassifiedFieldIsVerifiedRedactedNotJustTheResponsePath(logs);
        await MaskingScenarioAssertions.MaskingAppliesEvenWhenTheEventTypeHasNoRequiredReadClaimAtAll(registry, publish, follow);
        await MaskingScenarioAssertions.PartialRevealShowsOnlyTheConfiguredFirstAndLastCharactersPreservingSeparators(registry, publish, follow);
        await MaskingScenarioAssertions.HashMaskingIsCorrelatableAcrossEventsWithoutRevealingTheRealValue(registry, publish, follow);
        await MaskingScenarioAssertions.ARequiredNonNullableFieldIsStillMaskableWithNoNullWorkaround(registry, publish, follow);
        await MaskingScenarioAssertions.ALegitimatelyAbsentFieldStaysAbsentNotWrapped(registry, publish, follow);
        await MaskingScenarioAssertions.ScalarArrayWrapsEachElementAndComplexArrayWrapsOnlyTheMaskedPropertyPerElement(registry, publish, follow);
    }
}
