extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
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

// ADR-013 -- proves every error response is genuinely RFC 9457 Problem
// Details over real HTTP, not just that the code compiles: a bodied error
// (Results.Problem, explicit `type`/`title`/extensions) AND a bodyless one
// (Results.Forbid(), no code change at all -- AddProblemDetails() alone is
// what fills in a body for it) both need proving, since only the first is
// obviously testable from reading the source.
[TestClass]
public class ProblemDetailsHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-problemdetails-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

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

    private static async Task RegisterAsync(string appId, string eventType, string parentValidationMode = "Permissive")
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{eventType}")
        {
            Content = JsonContent.Create(new
            {
                appId, jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
                parentValidationMode,
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task AStrictModeUnresolvedParentReturnsProblemDetailsWithMissingParentEventIds()
    {
        const string appId = "problem-details-demo-1";
        await RegisterAsync(appId, "OrderShipped", "Strict");
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");

        var missingParentId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderShipped")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "OrderId": "order-1" }""", parentEventIds = new[] { missingParentId } }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("https://eventstore.example/problems/parent-not-found", problem.GetProperty("type").GetString());
        Assert.AreEqual(400, problem.GetProperty("status").GetInt32());
        Assert.AreEqual(missingParentId.ToString(), problem.GetProperty("missingParentEventIds")[0].GetString());
    }

    [TestMethod]
    public async Task AReusedEventIdWithDifferentContentReturnsProblemDetailsConflict()
    {
        const string appId = "problem-details-demo-2";
        await RegisterAsync(appId, "OrderPlaced");
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var eventId = Guid.NewGuid();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "OrderId": "order-2a" }""", eventId }),
        };
        AuthScenarioAssertions.AttachAuth(first, _hostClient, token, key);
        using var firstResponse = await _hostClient.SendAsync(first);
        Assert.AreEqual(HttpStatusCode.Accepted, firstResponse.StatusCode);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "OrderId": "order-2b" }""", eventId }),
        };
        AuthScenarioAssertions.AttachAuth(second, _hostClient, token, key);
        using var secondResponse = await _hostClient.SendAsync(second);

        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.AreEqual("application/problem+json", secondResponse.Content.Headers.ContentType!.MediaType);
        var problem = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("https://eventstore.example/problems/event-id-conflict", problem.GetProperty("type").GetString());
        Assert.AreEqual(eventId.ToString(), problem.GetProperty("eventId").GetString());
    }

    [TestMethod]
    public async Task PublishingAnUnregisteredEventTypeReturnsProblemDetailsNotFound()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/SomethingNeverRegistered")
        {
            Content = JsonContent.Create(new { appId = "problem-details-demo-3", schemaVersion = 1, payload = "{}" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("https://eventstore.example/problems/not-found", problem.GetProperty("type").GetString());
    }

    // No code at the PublishEndpoints.cs call site produces this body at
    // all -- PublishResult.Forbidden just returns the bare Results.Forbid()
    // it always has. This proves AddProblemDetails()/UseExceptionHandler()'s
    // OWN automatic behavior: a 4xx/5xx response written with no body gets
    // a real Problem Details body generated for it by the hosting layer,
    // not merely an empty 403.
    [TestMethod]
    public async Task AForbiddenPublishWithNoExplicitProblemDetailsCodeStillGetsOneAutomatically()
    {
        const string appId = "problem-details-demo-4";
        await RegisterAsync(appId, "OrderPlacedNoScope");
        // registry:admin, not events:publish -- RequireAuthorization("events:publish")
        // on the /publish/{eventType} route rejects this at the policy gate.
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlacedNoScope")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "OrderId": "order-4" }""" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType,
            "AddProblemDetails() should generate a real Problem Details body for this bodyless Forbid(), with no per-endpoint code at all");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(403, problem.GetProperty("status").GetInt32());
    }
}
