extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Replication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Sharding & Replication" (docs/08-build-plan.md) -- unlike
// ReplicationSqliteTests (PeerSyncReceiver exercised directly, proving the
// append/fold mechanism itself), this proves the actual wire path: real
// JSON serialization of PeerSyncPushRequest/Response, the peer:sync scope
// enforced by the real auth pipeline, and the /peer-sync/whoami handshake
// -- two real Host TestServers, each its own configured OriginId, talking
// over real (in-memory) HTTP.
[TestClass]
public class ReplicationHttpSqliteTests
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
        _dbPathA = Path.Combine(Path.GetTempPath(), $"eventstore-replication-http-a-{Guid.NewGuid():N}.db");
        _dbPathB = Path.Combine(Path.GetTempPath(), $"eventstore-replication-http-b-{Guid.NewGuid():N}.db");
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
    public async Task SiteAPushesAnEventToSiteBOverRealHttpAndSiteBAcknowledgesIt()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "peer-sync-client", "peer-sync-client-secret", "peer:sync events:publish registry:admin");

        // Register + publish at Site A through its own real HTTP surface,
        // the same as any ordinary client would.
        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderPlaced")
        {
            Content = JsonContent.Create(new
            {
                appId = "replication-http-demo", jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _hostClientA, token, key);
        var registerResponse = await _hostClientA.SendAsync(registerRequest);
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode);

        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId = "replication-http-demo", schemaVersion = 1, payload = """{ "OrderId": "rep-http-1", "Amount": 9.99 }""" }),
        };
        AuthScenarioAssertions.AttachAuth(publishRequest, _hostClientA, token, key);
        var publishResponse = await _hostClientA.SendAsync(publishRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, publishResponse.StatusCode);
        var published = (await publishResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("correlationId").GetGuid();

        // Drive PeerSyncWorker's own mechanics for real, over real HTTP, from
        // Site A to Site B -- a real PeerSyncClient whose "PeerSync" named
        // client is Site B's own TestServer HttpClient.
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = _hostClientB, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);

        var (whoAmIPeerId, _) = await peerSyncClient.WhoAmIAsync("", CancellationToken.None);
        Assert.AreEqual("site-b", whoAmIPeerId);

        var optionsA = new DbContextOptionsBuilder<EventStoreContext>().UseSqlite($"Data Source={_dbPathA}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")).Options;
        await using var dbA = new EventStoreContext(optionsA, new SqliteJsonPathTranslator());
        var events = await dbA.Events.AsNoTracking().OrderBy(e => e.SequenceNumber).ToListAsync();
        var pushRequest = new PeerSyncPushRequest("site-a", events.Select(PeerSyncWorker.ToPayload).ToList(), []);
        var pushResponse = await peerSyncClient.PushAsync("", pushRequest, CancellationToken.None);
        Assert.AreEqual(events[^1].SequenceNumber, pushResponse.AckedThroughSequenceNumber);

        var optionsB = new DbContextOptionsBuilder<EventStoreContext>().UseSqlite($"Data Source={_dbPathB}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")).Options;
        await using var dbB = new EventStoreContext(optionsB, new SqliteJsonPathTranslator());
        var replicated = await dbB.Events.AsNoTracking().SingleAsync(e => e.EventId == published);
        Assert.AreEqual("site-a", replicated.OriginId);
    }
}
