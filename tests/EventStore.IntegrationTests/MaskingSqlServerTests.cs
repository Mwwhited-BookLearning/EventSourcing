using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class MaskingSqlServerTests
{
    private static MsSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
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
            .UseSqlServer(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        return new EventStoreContext(options, new SqlServerJsonPathTranslator());
    }

    [TestMethod]
    public async Task AllMaskingScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqlServerFilterableFieldIndexDdlGenerator(), cache);
        var publish = new PublishService(db, registry, new SqlServerUniqueConstraintViolationDetector());
        var (payloadMasker, logs) = MaskingTestSupport.CreatePayloadMasker();
        var follow = new FollowService(db, new EventTailReader(db, registry, payloadMasker));

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
