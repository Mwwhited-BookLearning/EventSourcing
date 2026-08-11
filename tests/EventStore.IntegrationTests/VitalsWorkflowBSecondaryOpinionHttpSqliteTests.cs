extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventStore.Dpop;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Ucan;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;

namespace EventStore.IntegrationTests;

// Workflow B's "secondary opinion" half (docs/domains/clinical-trials-
// device-telemetry/features/adverse-event-capture-and-review.md, ADR-043)
// -- a genuinely separate, cross-process HTTP mechanism from the rest of
// Workflow B's in-process scenarios (VitalsWorkflowBScenarioAssertions.cs),
// the same real UcanDelegation-then-Token-Exchange flow
// DelegatedGrantsRbacFederationHttpSqliteTests.cs already proves for the
// core engine, applied here against a Vitals AdverseEvent entity.
//
// **A deliberate, documented divergence from the feature doc's own
// narrative**: that doc names the delegated claim "review:secondary-
// opinion" and describes a `QUERY liveAdverseEvent(entityId)` GraphQL
// field reading the whole Live View row. Neither exists for real --
// item 19's own build-scope note says explicitly "no generic entity/
// extensions: JSON query... nothing built here ever needs one," and no
// "liveAdverseEvent"/live-view query field exists anywhere in
// EventStore.GraphQL. The ONLY real, claims-gated, entity-scoped read
// mechanism this framework actually built is `revealField` (masked-field
// reveal, ADR-009/043), so this test exercises THAT instead: an AE's own
// masked SubjectId field, reusing the already-seeded "clinician-spa-
// client"/"colleague-client" pair and their real "clearance:phi" claim
// (auth.md) rather than seeding a new, unused "review:secondary-opinion"
// claim no client in this dev IdP actually holds.
[TestClass]
public class VitalsWorkflowBSecondaryOpinionHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-vitals-secondary-opinion-{Guid.NewGuid():N}.db");
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

    private static async Task RegisterAdverseEventReportedAsync(string appId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/AdverseEventReported")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "AeId": { "type": "string" }, "SubjectId": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "clearance:phi", "fixedValue": "REDACTED" } }, "Severity": { "type": "string" }, "SeriousAdverseEvent": { "type": "boolean" } }, "required": ["AeId", "SubjectId", "Severity", "SeriousAdverseEvent"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.AeId",
                entityType = "AdverseEvent",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> PublishAdverseEventAsync(string appId, string aeId, string subjectId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/AdverseEventReported")
        {
            Content = JsonContent.Create(new
            {
                appId, schemaVersion = 1,
                payload = $$"""{ "AeId": "{{aeId}}", "SubjectId": "{{subjectId}}", "Severity": "Severe", "SeriousAdverseEvent": true }""",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    private static async Task<JsonElement> RevealSubjectIdAsync(string appId, string aeId, Guid eventId, string token, DpopKeyPair key)
    {
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""mutation { revealField(entityId: "{{appId}}:adverseevent:{{aeId}}", eventId: "{{eventId}}", fieldPath: "$.SubjectId") { value } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> ExchangeAsync(string subjectToken, string appId, string clientId, string clientSecret, DpopKeyPair callerKey)
    {
        var tokenUrl = new Uri(_devIdpClient.BaseAddress!, "/connect/token").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = "urn:eventstore:token-type:external-subject",
                ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["app_id"] = appId,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }),
        };
        request.Headers.Add("DPoP", callerKey.CreateProof("POST", tokenUrl));
        return await _devIdpClient.SendAsync(request);
    }

    [TestMethod]
    public async Task APIDelegatesCappedEntityScopedSecondaryOpinionAccessAndTheColleagueCanRevealThePHIFieldForThatAEOnly()
    {
        const string appId = "vitals-secondary-opinion-1";
        await RegisterAdverseEventReportedAsync(appId);
        var ae1042 = await PublishAdverseEventAsync(appId, "ae-1042", "S-0091");
        var ae1039 = await PublishAdverseEventAsync(appId, "ae-1039", "S-0044"); // a DIFFERENT AE this grant must never cover
        await Task.Delay(500); // RouterWorker's own 200ms poll

        var (granterToken, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");

        var delegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", $"{appId}:adverseevent:ae-1042")],
            TimeSpan.FromMinutes(5), granterToken);

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, exchangeResponse.StatusCode, await exchangeResponse.Content.ReadAsStringAsync());
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var revealAe1042 = await RevealSubjectIdAsync(appId, "ae-1042", ae1042, grantedToken, granteeKey);
        Assert.IsFalse(revealAe1042.TryGetProperty("errors", out _), revealAe1042.ToString());
        Assert.AreEqual("S-0091", revealAe1042.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());

        // Entity-scoped, not blanket (ADR-043) -- the SAME grantee, the
        // SAME delegated claim, a DIFFERENT AE this grant never named.
        var revealAe1039 = await RevealSubjectIdAsync(appId, "ae-1039", ae1039, grantedToken, granteeKey);
        Assert.IsTrue(revealAe1039.TryGetProperty("errors", out _), revealAe1039.ToString());
    }

    [TestMethod]
    public async Task AStrangerWithNoGrantCannotRevealThePHIFieldEvenHoldingAnOrdinaryValidToken()
    {
        const string appId = "vitals-secondary-opinion-2";
        await RegisterAdverseEventReportedAsync(appId);
        var ae1042 = await PublishAdverseEventAsync(appId, "ae-1042", "S-0091");
        await Task.Delay(500);

        var (strangerToken, strangerKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");

        var reveal = await RevealSubjectIdAsync(appId, "ae-1042", ae1042, strangerToken, strangerKey);
        Assert.IsTrue(reveal.TryGetProperty("errors", out _), reveal.ToString());
        // "publisher-client" holds no "clearance:phi" claim at all (auth.md) --
        // the check is "does the caller have this claim, AND does it apply
        // to this EntityId" (ADR-043); an ordinary caller here has neither.
    }
}
