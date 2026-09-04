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
using RoleService = DevIdpAssembly::EventStore.DevIdp.RoleService;
using TrustRootService = DevIdpAssembly::EventStore.DevIdp.TrustRootService;

namespace EventStore.IntegrationTests;

// "Delegated Grants, RBAC, Federated Claims & Read Audit Logging"
// (docs/08-build-plan.md, ADR-043/044/046/047) -- a cross-process,
// real-HTTP flow (issuance/exchange at DevIdp, resolution at Host.Sqlite),
// the same reasoning AuthScenarioAssertions/TicketExchangeHttpSqliteTests'
// own HTTP-only test style already established. ADR-045 (AccessLog) has
// its own dedicated test file.
// [DoNotParallelize] -- this class's test methods share one static
// _hostClient/_dbPath (ClassInitialize, not per-test), the same class of
// interference under MSTestSettings.cs's method-level parallelism
// already fixed this session for GraphQlHttpSqliteTests,
// RbacProjectionWorkerHttpSqliteTests, TicketExchangeSecretRotationHttp
// SqliteTests, EntityQueryHttpSqliteTests, and BatchPublishHttpSqliteTests
// -- found here via two real, reproduced failures
// (ADelegatedGrantScopedToOneEntityPassesRequiredReadClaimForThatEntityOnlyNotBlanket,
// AFederatedTokenAugmentedWithLocalClaimsPassesRequiredReadClaimExactlyAsIfFromThePrimaryIdp)
// in a full-suite run, both passing cleanly every time in isolation.
[DoNotParallelize]
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

    // ADR-107 -- registers EventStore.Ucan's own UcanDelegationIssuedEventType,
    // the same "operator-client, registry:admin" shape RegisterPatientEnrolledAsync
    // above already uses for an ordinary, application-registered type.
    private static async Task RegisterUcanDelegationIssuedTypeAsync(string appId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{UcanDelegationIssuedEventType.Name}")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = UcanDelegationIssuedEventType.Schema,
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.GrantRef",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ADR-107 -- the real, opt-in audit step a granter application calls
    // AFTER UcanDelegation.Create succeeds, over the ordinary Publish API.
    // Uses publisher-client (holds events:publish) as the HTTP caller,
    // not the granter's own identity (clinician-spa-client, used
    // elsewhere in this file, holds no events:publish scope at all) --
    // the semantic granter/grantee are recorded in the payload's own
    // fields, independent of which credential actually POSTs the record,
    // the same distinction a real service publishing on behalf of a
    // business workflow would have.
    private static async Task PublishDelegationIssuedAsync(
        string appId, string granterActorId, string granteeActorId, IReadOnlyList<string> capabilityClaims, Guid grantRef, DateTimeOffset expiresAt)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{UcanDelegationIssuedEventType.Name}")
        {
            Content = JsonContent.Create(new
            {
                appId,
                schemaVersion = 1,
                payload = UcanDelegationIssuedEventType.BuildPayload(granterActorId, granteeActorId, capabilityClaims, grantRef, expiresAt),
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
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
    public async Task IssuingADelegationCanBeRecordedAsARealQueryableAuditEvent()
    {
        // ADR-107 -- resolves docs/10-open-questions.md's last row: issuance
        // stays fully offline (UcanDelegation.Create needs no network call,
        // unchanged below) -- this proves the SEPARATE, opt-in audit step a
        // real granter application calls afterward actually lands in the
        // Entity Store as a real, queryable event, not merely that the type
        // registers cleanly.
        const string appId = "delegated-grant-demo-5";
        await RegisterUcanDelegationIssuedTypeAsync(appId);

        var (granterToken, _) = await GetSubjectTokenAsync("clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");
        var grantRef = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var delegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", null)], expiresAt - DateTimeOffset.UtcNow, granterToken);
        Assert.IsFalse(string.IsNullOrEmpty(delegation));

        await PublishDelegationIssuedAsync(appId, "clinician-spa-client", "colleague-client", ["clearance:phi"], grantRef, expiresAt);
        await Task.Delay(500); // RouterWorker's own 200ms poll -- same real-Host wait every other GraphQL HTTP test already uses

        // `capabilities` (a JSON Schema "array"-typed property) is
        // deliberately never queried here -- EventTypeSchemaReader.cs's own
        // documented, pre-existing narrowing (08-build-plan.md): the
        // dynamic entity/payload GraphQL layer is scalar-only, an
        // "object"/"array"-typed top-level property is silently skipped,
        // not exposed as a field at all. Found for real (a genuine `does
        // not exist on the type` GraphQL error) while writing this test,
        // not assumed -- the 3 scalar fields below are still real,
        // independent proof the event landed correctly.
        var fieldName = $"entity_{appId.Replace('-', '_')}_{UcanDelegationIssuedEventType.Name.ToLowerInvariant()}";
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""query { {{fieldName}}(id: "{{grantRef}}") { granterActorId granteeActorId grantRef } }""",
            }), Encoding.UTF8, "application/json"),
        };
        var (readerToken, readerKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        AuthScenarioAssertions.AttachAuth(request, _hostClient, readerToken, readerKey);
        var response = await _hostClient.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.TryGetProperty("errors", out _), body.ToString());
        var entity = body.GetProperty("data").GetProperty(fieldName);
        Assert.AreEqual("clinician-spa-client", entity.GetProperty("granterActorId").GetString());
        Assert.AreEqual("colleague-client", entity.GetProperty("granteeActorId").GetString());
        Assert.AreEqual(grantRef.ToString(), entity.GetProperty("grantRef").GetString());
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

        // ADR-067 -- registering a trust root is now a Host-side, hash-
        // chained, registry:trust-admin-gated mutation; DevIdp's own local
        // AppTrustRoot table is populated by RbacProjectionWorker's Follow
        // fold in production, not synchronously by this call. Exercising
        // the LIVE cross-process worker from inside this same test process
        // hit a genuine WebApplicationFactory hazard (a background service
        // that self-references its own still-starting host -- see
        // RbacProjectionWorker's own header comment and TODO.md); this test
        // instead verifies the Host's real write path (scope-gated publish)
        // above, then applies the SAME fold DevIdp's worker would apply --
        // TrustRootService.RegisterAsync, unchanged -- directly, standing in
        // for the live Follow subscription.
        await RegisterTrustRootAsync(appId, appServiceKey.Thumbprint, "this app's own service identity");
        await ApplyTrustRootRegisteredFoldAsync(appId, appServiceKey.Thumbprint, "this app's own service identity");

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

        // "PUT /oauth/roles" (what "role-a" bundles) is unaffected by
        // ADR-067 -- it has no reserved event of its own, per that ADR's
        // Decision naming exactly 4 RBAC event types, none of them a role's
        // own permission-bundle definition.
        await PutAsync("/oauth/roles", new { appId, roleName = "role-a", permissions = new[] { "scoped:permA" } });
        // Assigning the role to an actor, and granting a direct permission,
        // ARE both reserved-event mutations now -- published via the Host's
        // real, scope-gated write path. In production, DevIdp's own
        // RbacProjectionWorker folds these into its local tables via a live
        // Follow subscription; this test applies the SAME fold target
        // methods (RoleService.AssignRoleAsync/GrantDirectPermissionAsync,
        // unchanged) directly rather than running that live cross-process
        // worker inside this test process -- see
        // ADelegationRootedInARegisteredAppTrustRootIsAcceptedForCustomPermissionsWithNoCentralPreRegistration's
        // own comment for why.
        await GrantRoleAsync(appId, actorId, "role-a");
        await ApplyRoleGrantedFoldAsync(appId, actorId, "role-a");
        await GrantPermissionAsync(appId, actorId, "direct:permB");
        await ApplyPermissionGrantedFoldAsync(appId, actorId, "direct:permB");

        var (tokenBefore, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decodedBefore = new JsonWebToken(tokenBefore);
        Assert.AreEqual("permA", decodedBefore.GetClaim("scoped").Value);
        Assert.AreEqual("permB", decodedBefore.GetClaim("direct").Value);

        await RevokeRoleAsync(appId, actorId, "role-a");
        await ApplyRoleRevokedFoldAsync(appId, actorId, "role-a");

        var (tokenAfter, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decodedAfter = new JsonWebToken(tokenAfter);
        // JsonWebToken.GetClaim throws (IDX14304) rather than returning null
        // for a claim that isn't present at all -- unlike ClaimsPrincipal's
        // own FindFirst -- found only by running this.
        Assert.IsFalse(decodedAfter.TryGetClaim("scoped", out _), "the revoked role's own permission is gone");
        Assert.AreEqual("permB", decodedAfter.GetClaim("direct").Value, "the direct, additive-only grant survives the unrelated role change");
    }

    // ADR-067 -- stands in for RbacProjectionWorker's own live Follow-based
    // fold (RoleService/TrustRootService calls, unchanged from the ones the
    // real worker uses), resolved from DevIdp's OWN service provider rather
    // than DevIdp's retired /oauth/role-assignments /oauth/user-permissions
    // /oauth/trust-roots HTTP endpoints. See TODO.md for why the live worker
    // itself isn't run inside these tests.
    private static async Task ApplyRoleGrantedFoldAsync(string appId, string actorId, string roleName)
    {
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>().AssignRoleAsync(actorId, appId, roleName);
    }

    private static async Task ApplyRoleRevokedFoldAsync(string appId, string actorId, string roleName)
    {
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>().RevokeRoleAsync(actorId, appId, roleName);
    }

    private static async Task ApplyPermissionGrantedFoldAsync(string appId, string actorId, string permission)
    {
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>().GrantDirectPermissionAsync(actorId, appId, permission);
    }

    private static async Task ApplyTrustRootRegisteredFoldAsync(string appId, string issuerDid, string? description)
    {
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TrustRootService>().RegisterAsync(appId, issuerDid, description);
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

    // ADR-067 -- RoleGranted/RoleRevoked/PermissionGranted/AppTrustRootRegistered
    // are now published through the Host's own EventStore.Rbac endpoints,
    // gated by registry:admin/registry:trust-admin, never written to DevIdp
    // directly. operator-client (DevIdpSeeder) holds both scopes.
    private static async Task GrantRoleAsync(string appId, string actorId, string roleName)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/rbac/roles/{roleName}/assignments")
        {
            Content = JsonContent.Create(new { appId, actorId }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task RevokeRoleAsync(string appId, string actorId, string roleName)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/rbac/roles/{roleName}/assignments?appId={appId}&actorId={actorId}");
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task GrantPermissionAsync(string appId, string actorId, string permission)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rbac/permissions")
        {
            Content = JsonContent.Create(new { appId, actorId, permission }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
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

    private sealed class JwksLookupHandler(ConcurrentDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
