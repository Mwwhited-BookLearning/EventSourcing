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
using TrustRootService = DevIdpAssembly::EventStore.DevIdp.TrustRootService;

namespace EventStore.IntegrationTests;

// Meridian's Workflow B -- Relying-Party Access
// (docs/domains/digital-identity-kyc/features/relying-party-
// verification-request.md, ADR-043) -- the same real UcanDelegation +
// OAuth Token Exchange mechanism VitalsWorkflowBSecondaryOpinionHttpSqliteTests.cs
// already proves for the core engine, applied here against a Meridian
// ApplicantIdentity entity. The doc's own narrative explicitly frames
// this as "the same mechanism applied to a new use case, not a new
// one," which is exactly what this test exercises.
//
// **Two real, documented divergences, not silent substitutions** (see
// docs/domains/README.md's own build-status note for the full
// reasoning): the doc's own `accessGrant`/`accessGrantRevoked` published-
// as-events plus a generic `QUERY { entity(id) { ... } }` GraphQL field
// have no real counterpart -- delegation is a client-signed UcanDelegation
// token, never a StoredEvent, and the only real claims-gated,
// entity-scoped read is `revealField` (masked-field reveal), the same gap
// already found building Vitals' own secondary-opinion access. There is
// also no real revocation-before-expiry mechanism (confirmed by search,
// already recorded) -- this test proves expiry instead, using a
// deliberately-past `exp` (safely beyond `TokenValidationParameters`'
// own 5-minute default clock skew, not a live wait).
//
// The customer's own freshly-generated DID key is registered as this
// AppId's own AppTrustRoot (ADR-044) -- a self-issued, root-of-trust
// delegation needing no pre-existing granter credential, which is
// exactly the shape "a customer signs a delegation with their own DID
// key" the feature doc's own narrative describes, realized for real.
[TestClass]
public class MeridianWorkflowBHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-meridian-relying-party-{Guid.NewGuid():N}.db");
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

    private static async Task RegisterIdentityClaimSubmittedAsync(string appId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/IdentityClaimSubmitted")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "ApplicantId": { "type": "string" }, "ClaimedLegalName": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "identity:pii-read", "fixedValue": "REDACTED" } } }, "required": ["ApplicantId", "ClaimedLegalName"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.ApplicantId",
                entityType = "ApplicantIdentity",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> PublishIdentityClaimAsync(string appId, string applicantId, string legalName)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/IdentityClaimSubmitted")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = $$"""{ "ApplicantId": "{{applicantId}}", "ClaimedLegalName": "{{legalName}}" }""" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    private static async Task<JsonElement> RevealClaimedLegalNameAsync(string appId, string applicantId, Guid eventId, string token, DpopKeyPair key)
    {
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""mutation { revealField(entityId: "{{appId}}:applicantidentity:{{applicantId}}", eventId: "{{eventId}}", fieldPath: "$.ClaimedLegalName") { value } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task RegisterTrustRootAsync(string appId, string issuerDid, string? description)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:trust-admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/rbac/trust-roots/{issuerDid}")
        {
            Content = JsonContent.Create(new { appId, description }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // Standing in for the live RbacProjectionWorker Follow fold (the same
    // WebApplicationFactory hazard DelegatedGrantsRbacFederationHttpSqliteTests.cs
    // already documents) -- applies the identical fold directly instead.
    private static async Task ApplyTrustRootRegisteredFoldAsync(string appId, string issuerDid, string? description)
    {
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TrustRootService>().RegisterAsync(appId, issuerDid, description);
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
    public async Task ACustomerDelegatesACappedEntityScopedTimeBoxedGrantAndTheRelyingPartyRevealsTheUnlockedFieldForThatApplicantOnly()
    {
        const string appId = "meridian-relying-party-1";
        await RegisterIdentityClaimSubmittedAsync(appId);
        var claim1001 = await PublishIdentityClaimAsync(appId, "applicant-1001", "Jane Smith");
        var claim1002 = await PublishIdentityClaimAsync(appId, "applicant-1002", "Someone Else"); // a DIFFERENT applicant this grant must never cover
        await Task.Delay(500);

        var customerKey = DpopKeyPair.Generate(); // stands in for applicant-1001's own DID key
        await RegisterTrustRootAsync(appId, customerKey.Thumbprint, "applicant-1001's own DID key");
        await ApplyTrustRootRegisteredFoldAsync(appId, customerKey.Thumbprint, "applicant-1001's own DID key");

        // No `prf` -- the customer's own key IS the root of trust for this
        // delegation (ADR-044), the same "self-verifying, signed with the
        // customer's own DID key, no server round-trip needed to create
        // it" shape the feature doc's own first sequence diagram describes.
        var delegation = UcanDelegation.Create(
            customerKey, "applicant-1001", "colleague-client", appId,
            [new DelegatedCapability("identity:pii-read", $"{appId}:applicantidentity:applicant-1001")],
            TimeSpan.FromHours(24));

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, exchangeResponse.StatusCode, await exchangeResponse.Content.ReadAsStringAsync());
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var reveal1001 = await RevealClaimedLegalNameAsync(appId, "applicant-1001", claim1001, grantedToken, granteeKey);
        Assert.IsFalse(reveal1001.TryGetProperty("errors", out _), reveal1001.ToString());
        Assert.AreEqual("Jane Smith", reveal1001.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());

        // Entity-scoped, not blanket (ADR-043) -- the same delegated claim,
        // a DIFFERENT applicant this grant never named.
        var reveal1002 = await RevealClaimedLegalNameAsync(appId, "applicant-1002", claim1002, grantedToken, granteeKey);
        Assert.IsTrue(reveal1002.TryGetProperty("errors", out _), reveal1002.ToString());
    }

    [TestMethod]
    public async Task AGrantCanBeUsedForMoreThanOneReadBeforeItExpires()
    {
        const string appId = "meridian-relying-party-2";
        await RegisterIdentityClaimSubmittedAsync(appId);
        var claim = await PublishIdentityClaimAsync(appId, "applicant-1001", "Jane Smith");
        await Task.Delay(500);

        var customerKey = DpopKeyPair.Generate();
        await RegisterTrustRootAsync(appId, customerKey.Thumbprint, null);
        await ApplyTrustRootRegisteredFoldAsync(appId, customerKey.Thumbprint, null);

        var delegation = UcanDelegation.Create(
            customerKey, "applicant-1001", "colleague-client", appId,
            [new DelegatedCapability("identity:pii-read", $"{appId}:applicantidentity:applicant-1001")],
            TimeSpan.FromHours(24));
        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var firstRead = await RevealClaimedLegalNameAsync(appId, "applicant-1001", claim, grantedToken, granteeKey);
        Assert.IsFalse(firstRead.TryGetProperty("errors", out _), firstRead.ToString());
        var secondRead = await RevealClaimedLegalNameAsync(appId, "applicant-1001", claim, grantedToken, granteeKey);
        Assert.IsFalse(secondRead.TryGetProperty("errors", out _), secondRead.ToString());
        Assert.AreEqual("Jane Smith", secondRead.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString(),
            "a grant is not consumed by one use -- only time or explicit revocation would end it");
    }

    [TestMethod]
    public async Task AnExpiredGrantFailsTokenExchangeAndNoReadEverOccurs()
    {
        const string appId = "meridian-relying-party-3";
        var customerKey = DpopKeyPair.Generate();
        await RegisterTrustRootAsync(appId, customerKey.Thumbprint, null);
        await ApplyTrustRootRegisteredFoldAsync(appId, customerKey.Thumbprint, null);

        // A deliberately-past exp -- 10 minutes ago, safely beyond
        // TokenValidationParameters' own 5-minute default clock skew, so
        // this is a deterministic assertion, not a live wait.
        var delegation = UcanDelegation.Create(
            customerKey, "applicant-1001", "colleague-client", appId,
            [new DelegatedCapability("identity:pii-read", $"{appId}:applicantidentity:applicant-1001")],
            TimeSpan.FromMinutes(-10));

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);

        Assert.AreEqual(HttpStatusCode.BadRequest, exchangeResponse.StatusCode);
        var body = await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("invalid_grant", body.GetProperty("error").GetString());
    }
}
