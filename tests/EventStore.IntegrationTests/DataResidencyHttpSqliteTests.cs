extern alias DevIdpAssembly;

using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Replication;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Data Residency (Region Pinning)" (docs/08-build-plan.md, ADR-061) --
// the one thing that genuinely needs real HTTP: PeerSyncWorker learns a
// destination peer's own tagged Region via the real /peer-sync/whoami
// handshake (ADR-061's own "rides along on the existing handshake, not a
// new discovery mechanism"), then filters what it pushes accordingly.
// Three real Host TestServers -- A (sender), B tagged "eu-west", C tagged
// "us-east" -- the same 2-site real-HTTP pattern ReplicationHttpSqliteTests
// already established, extended to a third site since proving "only the
// ALLOWED region receives it" needs a disallowed one to contrast against.
// One PER-TEST database for site A (a deliberate departure from this
// class's own shared-fixture Hosts B/C, found necessary by actually
// running this): PeerSyncCursor is keyed by PeerId, and StoredEvent.
// SequenceNumber is a single global counter across every AppId in one
// file -- three test methods sharing one dbA file under MSTest's 32-way
// parallelism raced on both, inflating one test's own expected sequence-
// number/cursor assertions with another concurrently-running test's own
// events. B and C stay class-level/shared -- every assertion against them
// checks presence by EventId (globally unique), never a sequence number
// or cursor value, so they're unaffected by other tests' own concurrent
// traffic landing in the same file.
[TestClass]
public class DataResidencyHttpSqliteTests
{
    private static string _dbPathB = default!;
    private static string _dbPathC = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactoryB = default!;
    private static WebApplicationFactory<Program> _hostFactoryC = default!;
    private static HttpClient _hostClientB = default!;
    private static HttpClient _hostClientC = default!;

    private string _dbPathA = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPathB = Path.Combine(Path.GetTempPath(), $"eventstore-residency-http-b-{Guid.NewGuid():N}.db");
        _dbPathC = Path.Combine(Path.GetTempPath(), $"eventstore-residency-http-c-{Guid.NewGuid():N}.db");
        await MigrateAsync(_dbPathB);
        await MigrateAsync(_dbPathC);

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(_devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        _hostFactoryB = BuildHostFactory(_dbPathB, "residency-site-b", "eu-west", devIdpConfiguration);
        _hostFactoryC = BuildHostFactory(_dbPathC, "residency-site-c", "us-east", devIdpConfiguration);
        _hostClientB = _hostFactoryB.CreateClient();
        _hostClientC = _hostFactoryC.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _hostClientB.Dispose();
        _hostClientC.Dispose();
        _hostFactoryB.Dispose();
        _hostFactoryC.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPathB, _dbPathC })
            if (File.Exists(path))
                File.Delete(path);
    }

    [TestInitialize]
    public async Task TestInit()
    {
        _dbPathA = Path.Combine(Path.GetTempPath(), $"eventstore-residency-http-a-{Guid.NewGuid():N}.db");
        await MigrateAsync(_dbPathA);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPathA))
            File.Delete(_dbPathA);
    }

    private static async Task MigrateAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using var db = new EventStoreContext(options, new SqliteJsonPathTranslator());
        await db.Database.MigrateAsync();
    }

    private static WebApplicationFactory<Program> BuildHostFactory(string dbPath, string originId, string region, OpenIdConnectConfiguration devIdpConfiguration) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={dbPath}");
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                    o.RequireHttpsMetadata = false;
                });
                services.Configure<OriginIdOptions>(o => o.OriginId = originId);
                services.Configure<RegionOptions>(o => o.Region = region);
            });
        });

    private EventStoreContext CreateContextA()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPathA}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    // Runs a full PeerSyncWorker.RunOnceAsync tick from Site A to exactly
    // ONE target (B or C), the same "" -relative-address-against-a-fixed-
    // TestServer-HttpClient trick ReplicationHttpSqliteTests already uses --
    // a real /peer-sync/whoami (learning that target's own tagged Region)
    // followed by a real, residency-filtered /peer-sync/push.
    private static async Task SyncOnceToAsync(EventStoreContext dbA, HttpClient targetHostClient, AppResidencyPolicyService residencyPolicies)
    {
        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions { SeedPeers = [""] }));
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = targetHostClient, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);

        await PeerSyncWorker.RunOnceAsync(dbA, peerSyncClient, addressBook, "residency-site-a", 500, residencyPolicies, NullLogger.Instance, CancellationToken.None);
    }

    [TestMethod]
    public async Task AnAppIdRestrictedToOneRegionReplicatesOnlyToPeersTaggedWithThatRegionEvenWhenBothAreOrdinaryReachablePeers()
    {
        const string appId = "residency-demo-1";
        await using var dbA = CreateContextA();
        var registryA = new SchemaRegistryService(dbA, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publishA = new PublishService(dbA, registryA, new SqliteUniqueConstraintViolationDetector());
        var residencyPoliciesA = new AppResidencyPolicyService(dbA, registryA, publishA);

        await registryA.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var policyResult = await residencyPoliciesA.SetAllowedRegionsAsync(appId, ["eu-west"], TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(policyResult);

        var published = (PublishResult.Accepted)await publishA.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "residency-1", "Amount": 10.00 }""", null, null), TestClaimsPrincipal.None);

        await SyncOnceToAsync(dbA, _hostClientB, residencyPoliciesA);
        await SyncOnceToAsync(dbA, _hostClientC, residencyPoliciesA);

        await using var dbB = OpenContext(_dbPathB);
        await using var dbC = OpenContext(_dbPathC);
        Assert.IsTrue(await dbB.Events.AnyAsync(e => e.EventId == published.CorrelationId), "eu-west is an allowed region -- the event must reach it");
        Assert.IsFalse(await dbC.Events.AnyAsync(e => e.EventId == published.CorrelationId), "us-east is NOT an allowed region -- the event must never reach it, even though it's an ordinary, reachable, configured peer");

        // The excluded event is never retried on a later tick -- the cursor
        // still advances past it, permanently.
        var cursorToC = await dbA.PeerSyncCursors.AsNoTracking().SingleAsync(c => c.PeerId == "residency-site-c");
        Assert.AreEqual(published.SequenceNumber, cursorToC.LastAckedSequenceNumber);
    }

    [TestMethod]
    public async Task AnAppIdWithNoAllowedRegionsConfiguredContinuesToReplicateToEveryPeerUnconstrained()
    {
        const string appId = "residency-demo-2";
        await using var dbA = CreateContextA();
        var registryA = new SchemaRegistryService(dbA, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publishA = new PublishService(dbA, registryA, new SqliteUniqueConstraintViolationDetector());
        var residencyPoliciesA = new AppResidencyPolicyService(dbA, registryA, publishA);

        await registryA.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        // No SetAllowedRegionsAsync call at all -- purely additive default.
        var published = (PublishResult.Accepted)await publishA.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "residency-2", "Amount": 5.00 }""", null, null), TestClaimsPrincipal.None);

        await SyncOnceToAsync(dbA, _hostClientB, residencyPoliciesA);
        await SyncOnceToAsync(dbA, _hostClientC, residencyPoliciesA);

        await using var dbB = OpenContext(_dbPathB);
        await using var dbC = OpenContext(_dbPathC);
        Assert.IsTrue(await dbB.Events.AnyAsync(e => e.EventId == published.CorrelationId), "unconstrained -- replicates everywhere, exactly as before this item existed");
        Assert.IsTrue(await dbC.Events.AnyAsync(e => e.EventId == published.CorrelationId));
    }

    [TestMethod]
    public async Task ARegionSatisfiedByOnlyOneLiveSiteIsSurfacedAsALogWarningNeverABlockedWriteOrHardFailure()
    {
        const string appId = "residency-demo-3";
        await using var dbA = CreateContextA();
        var registryA = new SchemaRegistryService(dbA, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publishA = new PublishService(dbA, registryA, new SqliteUniqueConstraintViolationDetector());
        var residencyPoliciesA = new AppResidencyPolicyService(dbA, registryA, publishA);

        await registryA.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        // Restricted to "eu-west" -- exactly ONE known live site (B) is
        // ever tagged with it in this fixture, never satisfying ADR-033's
        // own 2-replica minimum. Residency still wins -- the write below
        // succeeds and delivery to B (below) still happens; only a log
        // warning marks the unmet fault-tolerance goal, never a rejection.
        await residencyPoliciesA.SetAllowedRegionsAsync(appId, ["eu-west"], TestClaimsPrincipal.None);
        var published = await publishA.PublishAsync(
            "OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "residency-3", "Amount": 1.00 }""", null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(published, "residency wins, but it is never a blocked write");

        var logs = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning).AddProvider(logs));
        var logger = loggerFactory.CreateLogger("DataResidencyTest");

        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions { SeedPeers = [""] }));
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = _hostClientB, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);

        await PeerSyncWorker.RunOnceAsync(dbA, peerSyncClient, addressBook, "residency-site-a", 500, residencyPoliciesA, logger, CancellationToken.None);

        Assert.IsTrue(logs.Messages.Any(m => m.Contains(appId) && m.Contains("eu-west")),
            $"expected a warning naming the under-replicated AppId/region, got: {string.Join(" | ", logs.Messages)}");
    }

    private static EventStoreContext OpenContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }
}
