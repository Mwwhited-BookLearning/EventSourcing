extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "MVVM Client" (docs/08-build-plan.md, ADR-039) -- the two GraphQL-surface
// additions this item needs (registerViewDefinition/viewDefinition, and
// ConflictFlag/LateArrivalFlag/AuthorityStatus/SchemaVersion as fixed
// envelope fields on every dynamically-built Subscription payload) are only
// provably correct end to end, the same reasoning every other GraphQL HTTP
// test file in this repo already establishes. The client-web/ app itself
// (outbox durability, generic fallback rendering, the flag convention) has
// its own Vitest suite -- this file covers only the server-side surface it
// depends on.
[TestClass]
public class MvvmClientGraphQlHttpSqliteTests
{
    private static readonly HttpMethod Query = new("QUERY");

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-mvvm-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
        {
            await db.Database.MigrateAsync();

            // Seeded directly, before the Host's GraphQL schema ever starts --
            // FollowSubscriptionTypeModule's own hot-reload gap (TODO.md), the
            // same workaround every prior Subscription-over-HTTP test in this
            // repo already uses. viewDefinition/registerViewDefinition below
            // need no such workaround -- both are static resolvers reading/
            // writing the registry live.
            db.EventTypeDefinitions.Add(new EventStore.Domain.SchemaRegistry.EventTypeDefinition
            {
                AppId = "mvvm-http-demo-1",
                Name = "orderplaced",
                Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                RegisteredAt = DateTimeOffset.UtcNow,
                IsActive = true,
                EntityIdField = "$.OrderId",
                EntityType = "orderplaced",
                ChangeKind = EventStore.Domain.SchemaRegistry.ChangeKind.Full,
            });
            await db.SaveChangesAsync();
        }

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(_devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        _hostFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                o.RequireHttpsMetadata = false;
            }));
        });
        _hostClient = _hostFactory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _hostClient.Dispose();
        _hostFactory.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static async Task<Guid> PublishAsync(string appId, string eventType, string payload, int schemaVersion = 1)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion, payload }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    private static async Task<JsonElement> ExecuteGraphQlAsync(string query, string clientId, string clientSecret, string scope)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, clientSecret, scope);
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [TestMethod]
    public async Task RegisterViewDefinitionAndViewDefinitionQueryRoundTripOverRealHttp()
    {
        var registerResult = await ExecuteGraphQlAsync(
            """mutation { registerViewDefinition(entityType: "mvvm-http-order", viewKind: "Detail", compatibleSchemaVersions: [1], templateContent: "<div>{{orderId}}</div>") { version hash } }""",
            "operator-client", "operator-client-secret", "registry:admin");
        Assert.IsFalse(registerResult.TryGetProperty("errors", out _), registerResult.ToString());
        Assert.AreEqual(1, registerResult.GetProperty("data").GetProperty("registerViewDefinition").GetProperty("version").GetInt32());

        var queryResult = await ExecuteGraphQlAsync(
            """query { viewDefinition(entityType: "mvvm-http-order", viewKind: "Detail") { version templateContent } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(queryResult.TryGetProperty("errors", out _), queryResult.ToString());
        var viewDef = queryResult.GetProperty("data").GetProperty("viewDefinition");
        Assert.AreEqual(1, viewDef.GetProperty("version").GetInt32());
        Assert.AreEqual("<div>{{orderId}}</div>", viewDef.GetProperty("templateContent").GetString());

        // An EntityType with nothing registered returns null -- the client's own signal to fall back to the generic view.
        var missing = await ExecuteGraphQlAsync(
            """query { viewDefinition(entityType: "mvvm-http-order-never-registered", viewKind: "Detail") { version } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(missing.TryGetProperty("errors", out _), missing.ToString());
        Assert.AreEqual(JsonValueKind.Null, missing.GetProperty("data").GetProperty("viewDefinition").ValueKind);
    }

    [TestMethod]
    public async Task SubscriptionPayloadCarriesTheSharedEnvelopeFlagsAlongsideTheOrdinarySchemaFields()
    {
        const string appId = "mvvm-http-demo-1";

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        var subscriptionQuery = $$"""subscription { on_{{appId.Replace("-", "_")}}_orderplaced(mode: TAIL) { eventId orderId amount conflictFlag lateArrivalFlag authorityStatus schemaVersion } }""";

        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = subscriptionQuery }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        using var subscriptionResponse = await _hostClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var subscriptionReader = new StreamReader(await subscriptionResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(HttpStatusCode.OK, subscriptionResponse.StatusCode);

        var publishedEventId = await PublishAsync(appId, "OrderPlaced", """{ "OrderId": "mvvm-sub-1", "Amount": 42.5 }""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string? dataLine = null;
        while (!cts.IsCancellationRequested)
        {
            var line = await subscriptionReader.ReadLineAsync(cts.Token);
            if (line is null)
                break;
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataLine = line;
                break;
            }
        }

        Assert.IsNotNull(dataLine, "expected at least one SSE data frame carrying the published OrderPlaced event");
        var payload = JsonDocument.Parse(dataLine!["data: ".Length..]).RootElement;
        Assert.IsFalse(payload.TryGetProperty("errors", out _), payload.ToString());
        var orderPlaced = payload.GetProperty("data").GetProperty($"on_{appId.Replace("-", "_")}_orderplaced");
        Assert.AreEqual(publishedEventId.ToString(), orderPlaced.GetProperty("eventId").GetString());
        Assert.AreEqual("mvvm-sub-1", orderPlaced.GetProperty("orderId").GetString());
        Assert.AreEqual(42.5, orderPlaced.GetProperty("amount").GetDouble());
        Assert.IsFalse(orderPlaced.GetProperty("conflictFlag").GetBoolean());
        Assert.IsFalse(orderPlaced.GetProperty("lateArrivalFlag").GetBoolean());
        Assert.AreEqual("accepted", orderPlaced.GetProperty("authorityStatus").GetString());
        Assert.AreEqual(1, orderPlaced.GetProperty("schemaVersion").GetInt32());
    }
}
