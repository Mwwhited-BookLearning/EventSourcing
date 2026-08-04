using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class ReplicationPostgresTests
{
    private static PostgreSqlContainer _containerA = default!;
    private static PostgreSqlContainer _containerB = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _containerA = new PostgreSqlBuilder("postgres:18-alpine").Build();
        _containerB = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await Task.WhenAll(_containerA.StartAsync(), _containerB.StartAsync());
        using var dbA = CreateContext(_containerA);
        await dbA.Database.MigrateAsync();
        using var dbB = CreateContext(_containerB);
        await dbB.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await Task.WhenAll(_containerA.DisposeAsync().AsTask(), _containerB.DisposeAsync().AsTask());
    }

    private static EventStoreContext CreateContext(PostgreSqlContainer container)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
            .Options;
        return new EventStoreContext(options, new PostgresJsonPathTranslator());
    }

    private static SchemaRegistryService NewRegistry(EventStoreContext db) =>
        new(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

    private static PublishService NewPublishService(EventStoreContext db, SchemaRegistryService registry, string originId) =>
        new(db, registry, new PostgresUniqueConstraintViolationDetector(), Options.Create(new OriginIdOptions { OriginId = originId }));

    [TestMethod]
    public async Task AllReplicationScenarios()
    {
        using var dbA = CreateContext(_containerA);
        using var dbB = CreateContext(_containerB);
        var registryA = NewRegistry(dbA);
        var registryB = NewRegistry(dbB);
        var publishA = NewPublishService(dbA, registryA, "site-a");
        var publishB = NewPublishService(dbB, registryB, "site-b");

        await ReplicationScenarioAssertions.AnEventPublishedAtOneSiteEventuallyReplicatesToItsPeerWithOriginIdPreserved(registryA, publishA, dbA, registryB, dbB);
        await ReplicationScenarioAssertions.ASlowUploadingSiteNeverLosesQueuedEventsAcrossASimulatedRestart(registryA, publishA, dbA, () => CreateContext(_containerA));
        await ReplicationScenarioAssertions.TwoSitesDisconnectedAndIndependentlyWrittenToConvergeWithAGenuineConflictFlagged(registryA, publishA, dbA, registryB, publishB, dbB);
        ReplicationScenarioAssertions.APeerAddressLearnedFromAnotherPeersResponseIsMergedIntoTheLocalAddressBook();
        await ReplicationScenarioAssertions.AnEntityOfAGivenEntityTypeAlwaysResolvesToTheSameShardKey(registryA, publishA, dbA);
    }
}
