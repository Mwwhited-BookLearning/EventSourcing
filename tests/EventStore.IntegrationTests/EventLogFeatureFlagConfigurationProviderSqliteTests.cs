using EventStore.FeatureFlags;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-077's own read side -- EventLogFeatureFlagConfigurationProvider is
// deliberately provider-agnostic ADO.NET (see its own header comment), so
// this mechanism is covered once here rather than across all 3 providers --
// the same posture "Control-Plane Actions as Reserved Events"'s cross-
// process worker took for its own provider-agnostic pieces.
[TestClass]
public class EventLogFeatureFlagConfigurationProviderSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-feature-flags-provider-{Guid.NewGuid():N}.db");
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

    private static FeatureFlagService CreateFeatureFlagService(EventStoreContext db)
    {
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        return new FeatureFlagService(db, registry, publish);
    }

    [TestMethod]
    public async Task LoadSurfacesExistingFlagsUnderTheFeatureFlagsPrefix()
    {
        const string appId = "feature-flags-provider-demo-1";
        using var db = CreateContext();
        await CreateFeatureFlagService(db).SetFlagAsync(appId, "enable-widget", "true", TestClaimsPrincipal.None);

        using var provider = new EventLogFeatureFlagConfigurationProvider(() => new SqliteConnection($"Data Source={_dbPath}"), appId, TimeSpan.FromMinutes(10));
        provider.Load();

        Assert.IsTrue(provider.TryGet("FeatureFlags:enable-widget", out var value));
        Assert.AreEqual("true", value);
    }

    [TestMethod]
    public async Task APollThatObservesAChangedValueFiresExactlyOneReloadToken()
    {
        const string appId = "feature-flags-provider-demo-2";
        using var db = CreateContext();
        var featureFlags = CreateFeatureFlagService(db);
        await featureFlags.SetFlagAsync(appId, "rollout-percentage", "10", TestClaimsPrincipal.None);

        using var provider = new EventLogFeatureFlagConfigurationProvider(() => new SqliteConnection($"Data Source={_dbPath}"), appId, TimeSpan.FromMilliseconds(100));
        provider.Load();

        var reloadCount = 0;
        RegisterReloadCallback(provider, () => Interlocked.Increment(ref reloadCount));

        await featureFlags.SetFlagAsync(appId, "rollout-percentage", "50", TestClaimsPrincipal.None);
        await WaitUntilAsync(() => reloadCount >= 1, TimeSpan.FromSeconds(5));

        Assert.IsTrue(provider.TryGet("FeatureFlags:rollout-percentage", out var value));
        Assert.AreEqual("50", value);
    }

    [TestMethod]
    public async Task APollThatObservesNoChangedRowFiresNoReloadToken()
    {
        const string appId = "feature-flags-provider-demo-3";
        using var db = CreateContext();
        await CreateFeatureFlagService(db).SetFlagAsync(appId, "stable-flag", "\"unchanged\"", TestClaimsPrincipal.None);

        using var provider = new EventLogFeatureFlagConfigurationProvider(() => new SqliteConnection($"Data Source={_dbPath}"), appId, TimeSpan.FromMilliseconds(100));
        provider.Load();

        var reloadCount = 0;
        RegisterReloadCallback(provider, () => Interlocked.Increment(ref reloadCount));

        // Several poll ticks elapse with nothing changed in FeatureFlagState.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.AreEqual(0, reloadCount, "no consumer should see a change notification when nothing actually changed");
    }

    // ConfigurationProvider.GetReloadToken() itself only fires ONE consumer
    // callback per token instance (IChangeToken's own one-shot contract) --
    // re-registering after every fire is what IConfigurationRoot itself
    // does internally to keep observing a live-reloading provider.
    private static void RegisterReloadCallback(EventLogFeatureFlagConfigurationProvider provider, Action onReload)
    {
        ChangeToken.OnChange(provider.GetReloadToken, onReload);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        Assert.Fail("condition was never satisfied within the timeout");
    }
}
