extern alias DevIdpAssembly;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;

namespace EventStore.IntegrationTests;

// "Delegated Grants, RBAC, Federated Claims & Read Audit Logging"
// (docs/08-build-plan.md, ADR-043/044/046/047) -- a cross-process,
// real-HTTP flow (issuance/exchange at DevIdp, resolution at Host.Sqlite),
// the same reasoning AuthScenarioAssertions/TicketExchangeHttpSqliteTests'
// own HTTP-only test style already established. ADR-045 (AccessLog) has
// its own dedicated test file.
[TestClass]
public class DelegatedGrantsRbacFederationHttpSqliteTests
{
    private static readonly HttpMethod Query = new("QUERY");

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    // ADR-047's own federation test needs DevIdp's outbound JWKS fetch to
    // return a JSON document only knowable once that ONE test method has
    // generated its own throwaway EC keypair -- a ConcurrentDictionary
    // registered once, here, avoids ever rebuilding the shared
    // _devIdpFactory/_devIdpClient mid-test (which would silently corrupt
    // every OTHER test method sharing them, MSTest's own method-level
    // parallelism makes concurrent runs a real risk, not a theoretical one).
    private static readonly ConcurrentDictionary<string, string> JwksResponses = new();

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-delegated-grants-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => new JwksLookupHandler(JwksResponses))));
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

    private static async Task RegisterPatientEnrolledAsync(string appId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/PatientEnrolled")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "PatientId": { "type": "string" }, "Ssn": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "clearance:phi", "fixedValue": "REDACTED" } } }, "required": ["PatientId", "Ssn"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.PatientId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> PublishPatientAsync(string appId, string patientId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/PatientEnrolled")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = $$"""{ "PatientId": "{{patientId}}", "Ssn": "123-45-6789" }""" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    private static async Task<JsonElement> RevealSsnAsync(string appId, string patientId, Guid eventId, string token, DpopKeyPair key)
    {
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""mutation { revealField(entityId: "{{appId}}:patientenrolled:{{patientId}}", eventId: "{{eventId}}", fieldPath: "$.Ssn") { value } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ADR-043 step 1, driven directly (a real client signs this itself,
    // no DevIdp round trip at issuance time -- only at exchange).
    private static async Task<(string Token, string Jkt)> GetSubjectTokenAsync(string clientId, string clientSecret, string scope)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, clientSecret, scope);
        return (token, key.Thumbprint);
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
                // OpenIddict's own built-in RFC 8693 validation requires this
                // parameter to be present, AND -- found only by running this --
                // EVERY one of RFC 8693's own registered type names
                // (including the generic "jwt" one) triggers OpenIddict's
                // built-in signature check against ITS OWN signing keys
                // before this code ever runs, failing with ID2090 for every
                // subject_token here (a self-signed UCAN delegation or a
                // genuinely externally-issued federated token, never a
                // DevIdp-issued token). A custom URN (registered in
                // Program.cs's own SubjectTokenTypes set) gets the same
                // free pass "ticket" already gets for requested_token_type --
                // OpenIddict defers entirely to this code's own
                // sniff-the-JOSE-header branching below.
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
    public async Task ADelegatedGrantScopedToOneEntityPassesRequiredReadClaimForThatEntityOnlyNotBlanket()
    {
        const string appId = "delegated-grant-demo-1";
        await RegisterPatientEnrolledAsync(appId);
        var eventP1 = await PublishPatientAsync(appId, "p-1");
        var eventP2 = await PublishPatientAsync(appId, "p-2");
        await Task.Delay(500); // RouterWorker's own 200ms poll -- same real-Host wait every other GraphQL HTTP test already uses

        var (granterToken, granterJkt) = await GetSubjectTokenAsync("clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");
        Assert.AreEqual(granterKey.Thumbprint, granterJkt);

        var delegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", $"{appId}:patientenrolled:p-1")],
            TimeSpan.FromMinutes(5), granterToken);

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, exchangeResponse.StatusCode, await exchangeResponse.Content.ReadAsStringAsync());
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var revealP1 = await RevealSsnAsync(appId, "p-1", eventP1, grantedToken, granteeKey);
        Assert.IsFalse(revealP1.TryGetProperty("errors", out _), revealP1.ToString());
        Assert.AreEqual("123-45-6789", revealP1.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());

        var revealP2 = await RevealSsnAsync(appId, "p-2", eventP2, grantedToken, granteeKey);
        Assert.IsTrue(revealP2.TryGetProperty("errors", out _), revealP2.ToString());
    }

    [TestMethod]
    public async Task AnOverBroadDelegationFailsUcanValidationNotABespokeCheck()
    {
        const string appId = "delegated-grant-demo-2";
        var (granterToken, _) = await GetSubjectTokenAsync("clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");

        // clinician-spa-client's own claim set (DevIdpSeeder.ExtraClaims) is
        // exactly "clearance:phi" -- attempting to also delegate a claim it
        // was never granted itself.
        var delegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", null), new DelegatedCapability("clearance:megabroad", null)],
            TimeSpan.FromMinutes(5), granterToken);

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, exchangeResponse.StatusCode);
        var body = await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("invalid_grant", body.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task ADelegationRootedInARegisteredAppTrustRootIsAcceptedForCustomPermissionsWithNoCentralPreRegistration()
    {
        const string appId = "delegated-grant-demo-3";
        var appServiceKey = DpopKeyPair.Generate();

        var registerResponse = await _devIdpClient.PutAsync("/oauth/trust-roots",
            JsonContent.Create(new { appId, issuerDid = appServiceKey.Thumbprint, description = "this app's own service identity" }));
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode);

        // No `prf` at all -- the issuer key IS the root of trust, per
        // ADR-044's own text; "custom:widget-admin" was never registered
        // anywhere centrally, by design.
        var delegation = UcanDelegation.Create(
            appServiceKey, "app-service", "colleague-client", appId,
            [new DelegatedCapability("custom:widget-admin", null)],
            TimeSpan.FromMinutes(5));

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, exchangeResponse.StatusCode, await exchangeResponse.Content.ReadAsStringAsync());
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var decoded = new JsonWebToken(grantedToken);
        Assert.AreEqual("widget-admin", decoded.GetClaim("custom").Value);
    }

    [TestMethod]
    public async Task ADelegationWithNoProofRootedInAnUnregisteredKeyIsRejected()
    {
        const string appId = "delegated-grant-demo-4";
        var untrustedKey = DpopKeyPair.Generate(); // deliberately never registered as an AppTrustRoot for this AppId

        var delegation = UcanDelegation.Create(
            untrustedKey, "unregistered-service", "colleague-client", appId,
            [new DelegatedCapability("custom:widget-admin", null)],
            TimeSpan.FromMinutes(5));

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, exchangeResponse.StatusCode);
        var body = await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("invalid_grant", body.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task ADirectAdditiveOnlyUserPermissionSurvivesAnUnrelatedRoleChange()
    {
        const string appId = "rbac-demo-5";
        const string actorId = "colleague-client";

        await PutAsync("/oauth/roles", new { appId, roleName = "role-a", permissions = new[] { "scoped:permA" } });
        await PostAsync("/oauth/role-assignments", new { actorId, appId, roleName = "role-a" });
        await PostAsync("/oauth/user-permissions", new { actorId, appId, permission = "direct:permB" });

        var (tokenBefore, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decodedBefore = new JsonWebToken(tokenBefore);
        Assert.AreEqual("permA", decodedBefore.GetClaim("scoped").Value);
        Assert.AreEqual("permB", decodedBefore.GetClaim("direct").Value);

        var revokeResponse = await _devIdpClient.DeleteAsync($"/oauth/role-assignments?actorId={actorId}&appId={appId}&roleName=role-a");
        Assert.AreEqual(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var (tokenAfter, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decodedAfter = new JsonWebToken(tokenAfter);
        // JsonWebToken.GetClaim throws (IDX14304) rather than returning null
        // for a claim that isn't present at all -- unlike ClaimsPrincipal's
        // own FindFirst -- found only by running this.
        Assert.IsFalse(decodedAfter.TryGetClaim("scoped", out _), "the revoked role's own permission is gone");
        Assert.AreEqual("permB", decodedAfter.GetClaim("direct").Value, "the direct, additive-only grant survives the unrelated role change");
    }

    [TestMethod]
    public async Task AFederatedTokenAugmentedWithLocalClaimsPassesRequiredReadClaimExactlyAsIfFromThePrimaryIdp()
    {
        const string appId = "federated-claims-demo-6";
        await RegisterPatientEnrolledAsync(appId);
        var eventId = await PublishPatientAsync(appId, "p-fed-1");
        await Task.Delay(500);

        // A hand-constructed "external" issuer -- a fresh EC keypair, never
        // going through a second DevIdp process at all (this repo's own
        // "always real crypto, sometimes simplified transport" pattern --
        // the token IS genuinely EC-signed and genuinely verified against a
        // genuine JWKS document, just not fetched from a literal second
        // running service).
        var externalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string externalIssuer = "https://external-idp.example.org";
        var externalToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["iss"] = externalIssuer,
                ["sub"] = "ext-clinician-42",
                ["clearance"] = "phi", // the framework never checks WHICH IdP a "type:value" claim came from
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(externalKey), SecurityAlgorithms.EcdsaSha256),
        });

        const string jwksUri = "https://external-idp.example.org/.well-known/jwks";
        var parameters = externalKey.ExportParameters(includePrivateParameters: false);
        var jwksJson = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Base64UrlEncoder.Encode(parameters.Q.X),
                    y = Base64UrlEncoder.Encode(parameters.Q.Y),
                },
            },
        });

        // DevIdp's own FederationService fetches this via the default,
        // unnamed IHttpClientFactory client -- registered once in
        // ClassInit against JwksLookupHandler, populated here with exactly
        // this test's own throwaway key's JWKS document.
        JwksResponses[jwksUri] = jwksJson;

        var registerIssuerResponse = await _devIdpClient.PutAsync("/oauth/federation-issuers",
            JsonContent.Create(new { appId, issuer = externalIssuer, jwksUri, description = "test external IdP" }));
        Assert.AreEqual(HttpStatusCode.Created, registerIssuerResponse.StatusCode);

        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");
        var exchangeResponse = await ExchangeAsync(externalToken, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, exchangeResponse.StatusCode, await exchangeResponse.Content.ReadAsStringAsync());
        var grantedToken = (await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;

        var reveal = await RevealSsnAsync(appId, "p-fed-1", eventId, grantedToken, granteeKey);
        Assert.IsFalse(reveal.TryGetProperty("errors", out _), reveal.ToString());
        Assert.AreEqual("123-45-6789", reveal.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());
    }

    private static async Task PutAsync(string path, object body)
    {
        var response = await _devIdpClient.PutAsync(path, JsonContent.Create(body));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task PostAsync(string path, object body)
    {
        var response = await _devIdpClient.PostAsync(path, JsonContent.Create(body));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class JwksLookupHandler(ConcurrentDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
