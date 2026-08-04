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
public class MaskingPostgresTests
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
    public async Task AllMaskingScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector(), UpcastingTestSupport.CreateChain());
        var (payloadMasker, logs) = MaskingTestSupport.CreatePayloadMasker();
        var follow = new FollowService(db, new EventTailReader(db, registry, payloadMasker, UpcastingTestSupport.CreateChain()));

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
