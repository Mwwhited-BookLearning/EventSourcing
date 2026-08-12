extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventStore.Domain.SchemaRegistry;
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

// "Domain Decision Queues" follow-up, 2026-08-12 -- docs/10-open-questions.md's
// row on the generic entity/Live-View GraphQL query ADR-042/045 both assumed
// but "GraphQL-Only Query Layer" never built. Seeded directly at ClassInit,
// before the Host's GraphQL schema ever starts -- EntityQueryTypeModule (like
// FollowSubscriptionTypeModule before it) only builds its dynamic fields once,
// at schema warmup, the same pre-existing hot-reload limitation
// MvvmClientGraphQlHttpSqliteTests already works around the identical way.
[TestClass]
public class EntityQueryHttpSqliteTests
{
    private static readonly HttpMethod Query = new("QUERY");
    private const string AppId = "entityquery-demo-1";

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-entity-query-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
        {
            await db.Database.MigrateAsync();

            db.EventTypeDefinitions.Add(new EventTypeDefinition
            {
                AppId = AppId, Name = "widgetcreated", Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "WidgetId": { "type": "string" }, "Name": { "type": "string" }, "Secret": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "pii:view", "maskedValue": "REDACTED" } } }, "required": ["WidgetId", "Name", "Secret"] }""",
                RegisteredAt = DateTimeOffset.UtcNow, IsActive = true,
                EntityIdField = "$.WidgetId", EntityType = "widget", ChangeKind = ChangeKind.Full,
            });
            // Two distinct event types folding onto the SAME EntityType --
            // exercises EntityQueryTypeModule's own schema-merge logic.
            db.EventTypeDefinitions.Add(new EventTypeDefinition
            {
                AppId = AppId, Name = "gadgetcreated", Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "GadgetId": { "type": "string" }, "Label": { "type": "string" } }, "required": ["GadgetId", "Label"] }""",
                RegisteredAt = DateTimeOffset.UtcNow, IsActive = true,
                EntityIdField = "$.GadgetId", EntityType = "gadget", ChangeKind = ChangeKind.Partial,
            });
            db.EventTypeDefinitions.Add(new EventTypeDefinition
            {
                AppId = AppId, Name = "gadgetactivated", Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "GadgetId": { "type": "string" }, "ActivatedBy": { "type": "string" } }, "required": ["GadgetId", "ActivatedBy"] }""",
                RegisteredAt = DateTimeOffset.UtcNow, IsActive = true,
                EntityIdField = "$.GadgetId", EntityType = "gadget", ChangeKind = ChangeKind.Partial,
            });
            db.EventTypeDefinitions.Add(new EventTypeDefinition
            {
                AppId = AppId, Name = "restrictedthing", Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "ThingId": { "type": "string" }, "Value": { "type": "string" } }, "required": ["ThingId", "Value"] }""",
                RegisteredAt = DateTimeOffset.UtcNow, IsActive = true,
                EntityIdField = "$.ThingId", EntityType = "restrictedthing", ChangeKind = ChangeKind.Full,
                RequiredClaims = [new RequiredClaim { Direction = ClaimDirection.Read, Claim = "vault:access" }],
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

    private static EventStoreContext OpenDirectDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

    private static async Task<Guid> PublishAsync(string eventType, string payload, string? attestedActorId = null)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId = AppId, schemaVersion = 1, payload, attestedActorId }),
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
    public async Task AnAuthoritativeAcceptedEntityIsQueryableWithMaskingEnforcedPerCaller()
    {
        await PublishAsync("WidgetCreated", """{ "WidgetId": "w-1", "Name": "Sprocket", "Secret": "shh" }""");
        await Task.Delay(500); // RouterWorker's own 200ms poll

        const string query = """query { entity_entityquery_demo_1_widget(id: "w-1") { isAuthoritative authorityStatus name secret { value masked erased } } }""";

        // follower-client holds events:follow + pii:view -- the exact claim this Secret field's own x-masking requires.
        var withClaim = await ExecuteGraphQlAsync(query, "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(withClaim.TryGetProperty("errors", out _), withClaim.ToString());
        var withClaimEntity = withClaim.GetProperty("data").GetProperty("entity_entityquery_demo_1_widget");
        Assert.IsTrue(withClaimEntity.GetProperty("isAuthoritative").GetBoolean());
        Assert.AreEqual("accepted", withClaimEntity.GetProperty("authorityStatus").GetString());
        Assert.AreEqual("Sprocket", withClaimEntity.GetProperty("name").GetString());
        Assert.AreEqual("shh", withClaimEntity.GetProperty("secret").GetProperty("value").GetString());
        Assert.AreEqual(JsonValueKind.Null, withClaimEntity.GetProperty("secret").GetProperty("masked").ValueKind);

        // projections-client holds events:follow only, no pii:view.
        var withoutClaim = await ExecuteGraphQlAsync(query, "projections-client", "projections-client-secret", "events:follow");
        Assert.IsFalse(withoutClaim.TryGetProperty("errors", out _), withoutClaim.ToString());
        var withoutClaimEntity = withoutClaim.GetProperty("data").GetProperty("entity_entityquery_demo_1_widget");
        Assert.AreEqual(JsonValueKind.Null, withoutClaimEntity.GetProperty("secret").GetProperty("value").ValueKind);
        Assert.AreEqual("REDACTED", withoutClaimEntity.GetProperty("secret").GetProperty("masked").GetString());
    }

    [TestMethod]
    public async Task AnUnattestedEntityIsQueryableViaTheLiveViewWithIsAuthoritativeFalse()
    {
        await PublishAsync("WidgetCreated", """{ "WidgetId": "w-2", "Name": "Cog", "Secret": "shh2" }""", attestedActorId: "field-agent-1");
        await Task.Delay(500);

        var result = await ExecuteGraphQlAsync(
            """query { entity_entityquery_demo_1_widget(id: "w-2") { isAuthoritative authorityStatus name } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var entity = result.GetProperty("data").GetProperty("entity_entityquery_demo_1_widget");
        Assert.IsFalse(entity.GetProperty("isAuthoritative").GetBoolean(), "never accepted -- only the Live View has this data (ADR-042)");
        Assert.AreEqual("unattested", entity.GetProperty("authorityStatus").GetString());
        Assert.AreEqual("Cog", entity.GetProperty("name").GetString(), "the Live View folds every event immediately, no AuthorityStatus gate");
    }

    [TestMethod]
    public async Task MergedFieldsFromTwoContributingEventTypesAppearOnTheSameEntityQuery()
    {
        await PublishAsync("GadgetCreated", """{ "GadgetId": "g-1", "Label": "Widget-o-matic" }""");
        await Task.Delay(500);
        await PublishAsync("GadgetActivated", """{ "GadgetId": "g-1", "ActivatedBy": "operator-1" }""");
        await Task.Delay(500);

        var result = await ExecuteGraphQlAsync(
            """query { entity_entityquery_demo_1_gadget(id: "g-1") { label activatedBy } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var entity = result.GetProperty("data").GetProperty("entity_entityquery_demo_1_gadget");
        Assert.AreEqual("Widget-o-matic", entity.GetProperty("label").GetString(), "GadgetCreated's own contribution, from the FIRST contributing event type's schema");
        Assert.AreEqual("operator-1", entity.GetProperty("activatedBy").GetString(), "GadgetActivated's own contribution, merged from a SECOND event type sharing this EntityType");
    }

    [TestMethod]
    public async Task QueryingAnEntityRequiringAReadClaimIsForbiddenWithoutIt()
    {
        await PublishAsync("RestrictedThing", """{ "ThingId": "t-1", "Value": "classified" }""");
        await Task.Delay(500);

        var result = await ExecuteGraphQlAsync(
            """query { entity_entityquery_demo_1_restrictedthing(id: "t-1") { value } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsTrue(result.TryGetProperty("errors", out _), "follower-client holds no vault:access claim -- expected a Forbidden GraphQL error");
    }

    [TestMethod]
    public async Task QueryingANonexistentEntityReturnsNull()
    {
        var result = await ExecuteGraphQlAsync(
            """query { entity_entityquery_demo_1_widget(id: "does-not-exist") { name } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        Assert.AreEqual(JsonValueKind.Null, result.GetProperty("data").GetProperty("entity_entityquery_demo_1_widget").ValueKind);
    }

    [TestMethod]
    public async Task AnEntityQueryWritesAnAccessLogEntry()
    {
        await PublishAsync("WidgetCreated", """{ "WidgetId": "w-3", "Name": "Bolt", "Secret": "shh3" }""");
        await Task.Delay(500);

        await ExecuteGraphQlAsync(
            """query { entity_entityquery_demo_1_widget(id: "w-3") { name } }""",
            "follower-client", "follower-client-secret", "events:follow");

        await using var db = OpenDirectDb();
        var entityId = $"{AppId}:widget:w-3";
        var entry = await db.AccessLogEntries
            .Where(e => e.ResourceRef == entityId && e.Action == "read")
            .OrderByDescending(e => e.SequenceNumber)
            .FirstOrDefaultAsync();
        Assert.IsNotNull(entry, "expected an AccessLogEntry for this entity query (ADR-045)");
        Assert.AreEqual("follower-client", entry!.ReaderActorId);
        Assert.AreEqual("Authoritative", entry.ViewAccessed);
    }
}
