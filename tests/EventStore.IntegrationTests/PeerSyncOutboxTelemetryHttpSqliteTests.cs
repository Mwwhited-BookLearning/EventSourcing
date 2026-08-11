extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using EventStore.Domain.Observability;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Replication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Mechanism-Level OpenTelemetry Instrumentation" (docs/08-build-plan.md,
// ADR-088) -- the peer-sync outbox depth/age gauges. Deliberately its OWN
// test class/fixture, not a second [TestMethod] added to
// ReplicationHttpSqliteTests.cs: that file's ClassInit builds ONE shared
// pair of Hosts/SQLite files for the whole class, and this suite runs
// [TestMethod]s in parallel (MSTestSettings.cs's own
// ExecutionScope.MethodLevel) -- a second method there drives
// PeerSyncWorker.RunOnceAsync concurrently against the SAME two Host
// processes/SQLite files the original method is using, and one push
// intermittently 500s under that contention (found by actually running it
// together, not assumed). WebhookDeliveryHttpSqliteTests.cs's own header
// comment documents the identical class of bug for its own file, for the
// identical reason -- this file follows that same "give a resource-
// contentious HTTP fixture its own isolated class" precedent instead of
// risking the already-passing ReplicationHttpSqliteTests.cs scenario.
[TestClass]
public class PeerSyncOutboxTelemetryHttpSqliteTests
{
    private static string _dbPathA = default!;
    private static string _dbPathB = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactoryA = default!;
    private static WebApplicationFactory<Program> _hostFactoryB = default!;
    private static HttpClient _hostClientA = default!;
    private static HttpClient _hostClientB = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPathA = Path.Combine(Path.GetTempPath(), $"eventstore-peersync-otel-a-{Guid.NewGuid():N}.db");
        _dbPathB = Path.Combine(Path.GetTempPath(), $"eventstore-peersync-otel-b-{Guid.NewGuid():N}.db");
        await MigrateAsync(_dbPathA);
        await MigrateAsync(_dbPathB);

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(_devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        _hostFactoryA = BuildHostFactory(_dbPathA, "site-a", devIdpConfiguration);
        _hostFactoryB = BuildHostFactory(_dbPathB, "site-b", devIdpConfiguration);
        _hostClientA = _hostFactoryA.CreateClient();
        _hostClientB = _hostFactoryB.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _hostClientA.Dispose();
        _hostClientB.Dispose();
        _hostFactoryA.Dispose();
        _hostFactoryB.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPathA, _dbPathB })
            if (File.Exists(path))
                File.Delete(path);
    }

    private static async Task MigrateAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using var db = new EventStoreContext(options, new SqliteJsonPathTranslator());
        await db.Database.MigrateAsync();
    }

    private static WebApplicationFactory<Program> BuildHostFactory(string dbPath, string originId, OpenIdConnectConfiguration devIdpConfiguration) =>
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
            });
        });

    [TestMethod]
    public async Task PeerSyncOutboxDepthAndOldestPendingAgeAreReportedAsObservableGaugesPerPeerAfterARealTick()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "peer-sync-client", "peer-sync-client-secret", "peer:sync events:publish registry:admin");

        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderPlaced")
        {
            Content = JsonContent.Create(new
            {
                appId = "replication-http-otel", jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _hostClientA, token, key);
        var registerResponse = await _hostClientA.SendAsync(registerRequest);
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode);

        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId = "replication-http-otel", schemaVersion = 1, payload = """{ "OrderId": "rep-otel-1", "Amount": 9.99 }""" }),
        };
        AuthScenarioAssertions.AttachAuth(publishRequest, _hostClientA, token, key);
        var publishResponse = await _hostClientA.SendAsync(publishRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, publishResponse.StatusCode);

        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = _hostClientB, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);
        var addressBook = new PeerAddressBook(Options.Create(new PeerSyncOptions { SeedPeers = [""] }));

        var optionsA = new DbContextOptionsBuilder<EventStoreContext>().UseSqlite($"Data Source={_dbPathA}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")).Options;
        await using var dbA = new EventStoreContext(optionsA, new SqliteJsonPathTranslator());

        var (depthListener, depthMeasurements) = OpenTelemetryTestSupport.ListenForLongInstrument("duplex.peersync.outbox_depth");
        using var _1 = depthListener;
        var (ageListener, ageMeasurements) = OpenTelemetryTestSupport.ListenForLongInstrument("duplex.peersync.outbox_oldest_pending_age");
        using var _2 = ageListener;
        var (activityListener, activities) = OpenTelemetryTestSupport.ListenForActivity("duplex.peersync.outbox_pump");
        using var _3 = activityListener;

        await PeerSyncWorker.RunOnceAsync(dbA, peerSyncClient, addressBook, "site-a", batchSize: 100);
        depthListener.RecordObservableInstruments();
        ageListener.RecordObservableInstruments();

        var depthForSiteB = depthMeasurements.Where(m => m.HasTag("peer.id", "site-b")).ToList();
        var ageForSiteB = ageMeasurements.Where(m => m.HasTag("peer.id", "site-b")).ToList();
        Assert.HasCount(1, depthForSiteB, "one gauge reading per known peer");
        Assert.AreEqual(0L, depthForSiteB[0].Value, "the just-synced batch fully acknowledged site-b -- nothing remains pending");
        Assert.HasCount(1, ageForSiteB);
        Assert.IsGreaterThanOrEqualTo(1, activities.Count, "SyncOnceWithAsync must produce a named Activity, a distinct assertion from the gauge readings");
    }
}
