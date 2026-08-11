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

// "Bulk Ingestion & External Interchange-Format Adapters" (docs/08-build-
// plan.md, ADR-072) -- POST /publish/batch's own real-HTTP surface. Real
// HTTP is required here specifically because "the outer response is
// always 202" and "a malformed item's own 400-shaped rejection lives
// inside the response body" are both properties of the actual HTTP
// response shape, not something a direct PublishService call could prove.
[TestClass]
public class BatchPublishHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-batch-publish-http-{Guid.NewGuid():N}.db");
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

    private static async Task RegisterOrderPlacedAsync(string appId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderPlaced")
        {
            Content = JsonContent.Create(new
            {
                appId, jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task ABatchWithOneMalformedItemPersistsTheOthersAndReportsTheMalformedOneIndependentlyStaying202Throughout()
    {
        const string appId = "batch-publish-demo-1";
        await RegisterOrderPlacedAsync(appId);
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");

        // Item 2 is malformed -- missing the required "payload" field
        // entirely, not merely schema-invalid content.
        var body = $$"""
            [
              { "eventType": "OrderPlaced", "request": { "appId": "{{appId}}", "schemaVersion": 1, "payload": "{ \"OrderId\": \"batch-1a\", \"Amount\": 1.00 }" } },
              { "eventType": "OrderPlaced", "request": { "appId": "{{appId}}", "schemaVersion": 1 } },
              { "eventType": "OrderPlaced", "request": { "appId": "{{appId}}", "schemaVersion": 1, "payload": "{ \"OrderId\": \"batch-1c\", \"Amount\": 3.00 }" } }
            ]
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/batch") { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, "the OUTER response stays 202 even though one item is malformed");

        var results = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.AreEqual(3, results.Count, "one result per submitted item, in submission order");
        Assert.AreEqual(202, results[0].GetProperty("httpStatus").GetInt32());
        Assert.AreEqual(400, results[1].GetProperty("httpStatus").GetInt32(), "the malformed item reports its own 400-shaped rejection independently");
        Assert.AreEqual(202, results[2].GetProperty("httpStatus").GetInt32(), "a later, well-formed item is unaffected by an earlier malformed one");

        var seq1 = results[0].GetProperty("sequenceNumber").GetInt64();
        var seq3 = results[2].GetProperty("sequenceNumber").GetInt64();
        Assert.IsTrue(seq3 > seq1, "distinct, increasing sequence numbers for the persisted items");

        await using var db = OpenDb();
        Assert.AreEqual(2, await db.Events.CountAsync(e => e.AppId == appId && e.EventType == "orderplaced"), "exactly the two well-formed items actually persisted");
    }

    [TestMethod]
    public async Task ASchemaInvalidButNotMalformedEventInsideABatchStillPersistsWithAnAdvisorySchemaStatus()
    {
        const string appId = "batch-publish-demo-2";
        await RegisterOrderPlacedAsync(appId);
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");

        // Well-formed batch item (valid JSON, all required envelope fields
        // present), but its OWN payload doesn't conform to OrderPlaced's
        // registered schema (Amount is a string, not a number) -- schema
        // conformance is the Router's own advisory concern, never a
        // publish-time rejection (ADR-023).
        var body = $$"""
            [ { "eventType": "OrderPlaced", "request": { "appId": "{{appId}}", "schemaVersion": 1, "payload": "{ \"OrderId\": \"batch-2a\", \"Amount\": \"not-a-number\" }" } } ]
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/batch") { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        var results = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.AreEqual(202, results[0].GetProperty("httpStatus").GetInt32(), "schema-invalid content is never rejected, only advisory");

        await using var db = OpenDb();
        var stored = await db.Events.SingleAsync(e => e.AppId == appId && e.EventType == "orderplaced");
        Assert.AreEqual("received", stored.Status, "durably persisted regardless of schema conformance");
    }

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());
}
