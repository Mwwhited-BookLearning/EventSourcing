extern alias DevIdpAssembly;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Inbox;
using EventStore.Interchange.Abstractions;
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

// "Tenant-to-Tenant Federation Mapping" (docs/08-build-plan.md, ADR-082)
// -- confirms federation needs NO new mechanism: tenant A authenticates
// to tenant B's deployment via the SAME ordinary client_credentials flow
// every other caller already uses (publisher-client, unchanged -- no
// federation-specific credential type), and a bespoke, per-tenant-pair
// mapping is registered as an ORDINARY custom IInterchangeFormatAdapter
// in tenant B's own composition root (simulated here via
// WebApplicationFactory.ConfigureServices, standing in for "a deployment
// team's own Program.cs," never a change to any core EventStore.*
// project) -- resolved through the SAME generic /interchange/{adapterKey}/
// {appId} endpoint "Bulk Ingestion & External Interchange-Format
// Adapters" (item 36) already built for FHIR, not a second, federation-
// specific endpoint.
[TestClass]
public class TenantFederationHttpSqliteTests
{
    // A fictitious tenant A's own native shape -- deliberately NOT
    // shaped like tenant B's registered OrderPlaced event at all, so a
    // successful round trip can only mean the mapping adapter actually
    // ran, not a coincidental field-name match.
    private const string TenantAAdapterKey = "TenantAOrderMapping";

    private sealed class TenantAOrderMappingAdapter : IInterchangeFormatAdapter
    {
        public Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default)
        {
            var tenantAShape = JsonNode.Parse(rawMessage) as JsonObject ?? throw new FormatException("expected a tenant-A-shaped JSON object");
            var legacyRef = tenantAShape["LegacyOrderRef"]?.GetValue<string>() ?? throw new FormatException("missing LegacyOrderRef");
            var totalCents = tenantAShape["TotalCents"]?.GetValue<long>() ?? throw new FormatException("missing TotalCents");

            var mapped = JsonSerializer.Serialize(new { OrderId = legacyRef, Amount = totalCents / 100.0 });
            return Task.FromResult(new InterchangeInboundResult("OrderPlaced", mapped, ReviewPending: false));
        }

        public Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default) =>
            throw new NotSupportedException("this tenant-pair mapping is inbound-only");
    }

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-tenant-federation-http-{Guid.NewGuid():N}.db");
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

        // This ConfigureServices block IS "tenant B's own composition
        // root" for the purposes of this test -- registering one MORE
        // keyed adapter beyond whatever EventStore.Interchange's own
        // AddInterchange() already provides, exactly as ADR-082's own
        // text describes, never a change to a shared core project.
        _hostFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                    o.RequireHttpsMetadata = false;
                });
                services.AddKeyedScoped<IInterchangeFormatAdapter, TenantAOrderMappingAdapter>(TenantAAdapterKey);
            });
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

    [TestMethod]
    public async Task TenantAAuthenticatesWithAnOrdinaryClientCredentialsTokenAndItsNativeShapeArrivesMappedNeverRaw()
    {
        const string appId = "tenant-b-demo";

        // Tenant B's own registered event type -- its own native shape,
        // unrelated to tenant A's own.
        var (operatorToken, operatorKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderPlaced")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                appId, jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _hostClient, operatorToken, operatorKey);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(registerRequest)).StatusCode);

        // Tenant A authenticates with an ORDINARY client_credentials
        // token -- publisher-client, the same credential type/flow every
        // other caller in this repo already uses, confirming no new
        // authentication mechanism was introduced for federation.
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var tenantANativeShape = """{ "LegacyOrderRef": "tenant-a-ord-77", "TotalCents": 15050 }""";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/interchange/{TenantAAdapterKey}/{appId}")
        {
            Content = new StringContent(tenantANativeShape, Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        await using var db = OpenDb();
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == "orderplaced");
        // The MAPPED shape landed in tenant B's Event Log -- never
        // tenant A's own raw LegacyOrderRef/TotalCents fields.
        Assert.IsTrue(stored.Payload.Contains("tenant-a-ord-77"));
        Assert.IsTrue(stored.Payload.Contains("150.5") || stored.Payload.Contains("150.50"), stored.Payload);
        Assert.IsFalse(stored.Payload.Contains("LegacyOrderRef"), "the raw cross-tenant shape must never land in tenant B's own Event Log, only the mapped one");
        Assert.IsFalse(stored.Payload.Contains("TotalCents"));
    }

    [TestMethod]
    public async Task AMistargetedAdapterKeyIsRejected404RatherThanFallingBackToAnyRegisteredAdapter()
    {
        const string appId = "tenant-b-demo-2";
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/interchange/NoSuchTenantMapping/{appId}") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());
}
