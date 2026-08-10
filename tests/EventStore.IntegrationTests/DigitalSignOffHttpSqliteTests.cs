extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Headers;
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

// "Digital Sign-Off for Regulated Actions (Step-Up Authentication)" (docs/
// 08-build-plan.md, ADR-066) -- RFC 9470's own WWW-Authenticate challenge is
// an actual HTTP response header, only provably correct over a real HTTP
// round trip, the same "auth is pipeline/middleware behavior" reasoning
// AuthScenarioAssertions' own HTTP-only style already established. Single
// provider (Sqlite) -- none of this item's own logic (claims/header
// shaping) is DB-provider-specific, same reasoning "SPIFFE/SPIRE" used.
[TestClass]
public class DigitalSignOffHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-signoff-http-{Guid.NewGuid():N}.db");
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
            builder.UseSetting("Authentication:Authority", _devIdpClient.BaseAddress!.ToString());
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

    private static async Task RegisterSignedTypeAsync(string appId, string typeName, string acrValue)
    {
        var (operatorToken, operatorKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{typeName}")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.Id",
                parentValidationMode = "Permissive",
                requiredSignature = new { acrValues = new[] { acrValue }, maxAge = (int?)null },
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, operatorToken, operatorKey);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task APublishWithNoStepUpIsRejected401WithARfc9470ChallengeHeaderNamingTheRequiredAcrValues()
    {
        const string appId = "signoff-http-demo-1";
        const string typeName = "RequiresStepUpHttp1";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterSignedTypeAsync(appId, typeName, acrValue);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "Id": "rec-1" }""", meaning = "approved" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        var response = await _hostClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.SingleOrDefault(h => h.Scheme == "Bearer");
        Assert.IsNotNull(challenge, "RFC 9470's own challenge, a Bearer-scheme WWW-Authenticate header, must be present on a 401 step-up rejection");
        Assert.Contains("error=\"insufficient_user_authentication\"", challenge.Parameter);
        Assert.Contains($"acr_values=\"{acrValue}\"", challenge.Parameter);
    }

    [TestMethod]
    public async Task RequestingAStepUpTokenFromDevIdpAndRetryingWithMeaningSucceeds()
    {
        const string appId = "signoff-http-demo-2";
        const string typeName = "RequiresStepUpHttp2";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterSignedTypeAsync(appId, typeName, acrValue);

        // Simulates the RFC 9470 client-side redirect-and-retry loop: the
        // caller took the challenge above (not repeated here, already
        // proven by the previous test) back through the IdP to step up, and
        // is retrying with a token that now satisfies it.
        var (steppedUpToken, key) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "publisher-client", "publisher-client-secret", "events:publish", acr: acrValue);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "Id": "rec-1" }""", meaning = "approved" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, steppedUpToken, key);

        var response = await _hostClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AStepUpSatisfyingPublishThatOmitsMeaningIsRejected400AsAnIncompleteEnvelope()
    {
        const string appId = "signoff-http-demo-3";
        const string typeName = "RequiresStepUpHttp3";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterSignedTypeAsync(appId, typeName, acrValue);

        var (steppedUpToken, key) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "publisher-client", "publisher-client-secret", "events:publish", acr: acrValue);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "Id": "rec-1" }""" }), // meaning omitted
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, steppedUpToken, key);

        var response = await _hostClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
