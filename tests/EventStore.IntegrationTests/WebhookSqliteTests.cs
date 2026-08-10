using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Webhooks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class WebhookSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-webhooks-{Guid.NewGuid():N}.db");
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
    public async Task AllWebhookScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        await WebhookScenarioAssertions.RegisteringASubscriptionFreezesItsClaimSnapshotOnce(db, subscriptions);
        await WebhookScenarioAssertions.AMatchingEventIsMaskedAndEnqueuedIntoTheDurableOutbox(db, registry, publish, subscriptions, upcastChain, payloadMasker);
        await WebhookScenarioAssertions.ANonMatchingEventTypeIsNeverEnqueuedForThatSubscription(db, registry, publish, subscriptions, upcastChain, payloadMasker);
    }

    // Pure WebhookRetryTracker logic, no db/HTTP involved at all -- covered
    // once here, not repeated per provider, the same "no provider-specific
    // behavior to re-prove" reasoning GraphQlFilterPredicateBuilderSqliteTests
    // already uses for its own SQLite-only scope.
    [TestMethod]
    public void SuccessiveFailuresAreSpacedFurtherApartThanTheLastByExponentialBackoff()
    {
        var tracker = new WebhookRetryTracker();
        var subscriptionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var initialBackoff = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromMinutes(10);

        // Jitter (0-250ms) means the exact gap varies, but the FLOOR
        // (backoff alone, before jitter) must strictly grow each time:
        // 1s, then 2s, then 4s -- checked right after each own RecordFailure
        // call, since a later call OVERWRITES this key's tracked state with
        // that call's own new window, not accumulating past ones.
        Assert.AreEqual(1, tracker.RecordFailure(subscriptionId, 1, initialBackoff, maxBackoff, now));
        Assert.IsTrue(tracker.ShouldWait(subscriptionId, 1, now + TimeSpan.FromMilliseconds(999)), "the first failure's own backoff floor is ~1s");

        Assert.AreEqual(2, tracker.RecordFailure(subscriptionId, 1, initialBackoff, maxBackoff, now));
        Assert.IsTrue(tracker.ShouldWait(subscriptionId, 1, now + TimeSpan.FromMilliseconds(1900)), "the second failure's own backoff floor (~2s) is longer than the first's (~1s)");

        Assert.AreEqual(3, tracker.RecordFailure(subscriptionId, 1, initialBackoff, maxBackoff, now));
        Assert.IsTrue(tracker.ShouldWait(subscriptionId, 1, now + TimeSpan.FromMilliseconds(3900)), "the third failure's own backoff floor (~4s) is longer than the second's (~2s)");
    }
}
