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

// "Compatibility & Deployment Discipline" (docs/08-build-plan.md, ADR-038):
// the two mechanisms this item adds that are genuinely GraphQL-surface
// behavior -- the enum-fallback contract's {value, valueKnown} sibling
// field (FollowSubscriptionTypeModule) and the capabilities(...) version-
// negotiation query (CapabilitiesQueries) -- only provably correct end to
// end, the same "auth/pipeline behavior" reasoning GraphQlHttpSqliteTests'
// own HTTP-only test style already established. The rollback-drill exit
// criterion itself (RouterWorker's forward-incompatibility gate) is
// covered separately by CompatibilityScenarioAssertions against all three
// providers -- it needs no HTTP surface at all.
[TestClass]
public class CompatibilityGraphQlHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-compat-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
        {
            await db.Database.MigrateAsync();

            // Seeded directly, before the Host's GraphQL schema ever starts --
            // FollowSubscriptionTypeModule's own hot-reload gap (TODO.md),
            // same workaround GraphQlHttpSqliteTests' own ClassInit already
            // uses. capabilities(...) below needs no such workaround -- it's
            // a static resolver reading the registry live, not a
            // dynamically-built Subscription field.
            db.EventTypeDefinitions.Add(new EventStore.Domain.SchemaRegistry.EventTypeDefinition
            {
                AppId = "compat-http-demo-1",
                Name = "orderstatuschanged",
                Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Status": { "type": "string", "enum": ["Placed", "Shipped", "Delivered"], "x-enum-fallback": true } }, "required": ["OrderId", "Status"] }""",
                RegisteredAt = DateTimeOffset.UtcNow,
                IsActive = true,
                EntityIdField = "$.OrderId",
                EntityType = "orderstatuschanged",
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

    private static async Task RegisterAsync(string appId, string eventType, string jsonSchema, string entityIdField, string changeKind = "Full")
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{eventType}")
        {
            Content = JsonContent.Create(new { appId, jsonSchema, filterableFields = Array.Empty<object>(), changeKind, entityIdField }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
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
    public async Task AnEnumFallbackFieldCarriesTheRawValueAlongsideAKnownFlagForAnUnrecognizedValue()
    {
        // "OrderStatusChanged" for this appId is seeded directly in
        // ClassInit -- see that method's own note.
        const string appId = "compat-http-demo-1";

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        var subscriptionQuery = $$"""subscription { on_{{appId.Replace("-", "_")}}_orderstatuschanged(mode: TAIL) { status statusKnown } }""";

        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = subscriptionQuery }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        using var subscriptionResponse = await _hostClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var subscriptionReader = new StreamReader(await subscriptionResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(HttpStatusCode.OK, subscriptionResponse.StatusCode);

        // "PartiallyRefunded" was never declared in this schema's own
        // "enum" list -- ADR-038's own scenario ("a value added after this
        // client's own build").
        await PublishAsync(appId, "OrderStatusChanged", """{ "OrderId": "compat-sub-1", "Status": "PartiallyRefunded" }""");

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

        Assert.IsNotNull(dataLine, "expected at least one SSE data frame carrying the published OrderStatusChanged event");
        var payload = JsonDocument.Parse(dataLine!["data: ".Length..]).RootElement;
        Assert.IsFalse(payload.TryGetProperty("errors", out _), payload.ToString());
        var orderStatusChanged = payload.GetProperty("data").GetProperty($"on_{appId.Replace("-", "_")}_orderstatuschanged");
        Assert.AreEqual("PartiallyRefunded", orderStatusChanged.GetProperty("status").GetString(), "the raw string travels through unmodified, never substituted or dropped");
        Assert.IsFalse(orderStatusChanged.GetProperty("statusKnown").GetBoolean(), "not in this schema's own declared \"enum\" list");
    }

    [TestMethod]
    public async Task CapabilitiesQueryAcceptsAClientInsideTheNMinus1NPlus1WindowAndRejectsOneOutsideIt()
    {
        const string appId = "compat-http-demo-2";
        // Three sequential registrations reach active version 3, the same
        // "OrderPlaced" event type each time (SchemaRegistryService.
        // RegisterAsync always increments from the prior active version).
        for (var i = 0; i < 3; i++)
            await RegisterAsync(appId, "OrderPlaced", """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""", "$.OrderId");

        var accepted = await ExecuteGraphQlAsync(
            $$"""query { capabilities(appId: "{{appId}}", name: "OrderPlaced", supportedSchemaVersions: [2, 3]) { activeVersion supportedWindow } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(accepted.TryGetProperty("errors", out _), accepted.ToString());
        var capabilities = accepted.GetProperty("data").GetProperty("capabilities");
        Assert.AreEqual(3, capabilities.GetProperty("activeVersion").GetInt32());
        var window = capabilities.GetProperty("supportedWindow").EnumerateArray().Select(v => v.GetInt32()).ToList();
        CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, window);

        var rejected = await ExecuteGraphQlAsync(
            $$"""query { capabilities(appId: "{{appId}}", name: "OrderPlaced", supportedSchemaVersions: [1]) { activeVersion } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsTrue(rejected.TryGetProperty("errors", out _), rejected.ToString());
    }
}
