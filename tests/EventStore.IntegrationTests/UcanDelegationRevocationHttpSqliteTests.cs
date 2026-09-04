extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using EventStore.Dpop;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Projections.Host;
using EventStore.Ucan;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;
using RbacProjectionWorker = DevIdpAssembly::EventStore.DevIdp.RbacProjectionWorker;
using RbacProjectionOptions = DevIdpAssembly::EventStore.DevIdp.RbacProjectionOptions;

namespace EventStore.IntegrationTests;

// ADR-104 -- proves the live revocation check is real, running code, not
// merely designed: issue a real delegation, exchange it successfully,
// revoke it via the real POST /ucan/delegations/{grantRef}/revoke
// endpoint (EventStore.Rbac), fold the resulting UcanDelegationRevoked
// event through the real Follow subscription (RbacProjectionWorker.
// CatchUpOnceAsync, the same test-harness pattern
// RbacProjectionWorkerHttpSqliteTests.cs already established for exactly
// this "drive the fold directly, post-ClassInit" hazard), then attempt
// the SAME delegation again and confirm it is now genuinely rejected.
//
// [DoNotParallelize] -- same class of shared-fixture-state hazard
// RbacProjectionWorkerHttpSqliteTests.cs/DelegatedGrantsRbacFederation
// HttpSqliteTests.cs's own header comments already document for this
// exact HttpClient/WebApplicationFactory sharing shape.
[TestClass]
[DoNotParallelize]
public class UcanDelegationRevocationHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-ucan-revocation-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>().WithWebHostBuilder(builder =>
        {
            // Same FollowClientOptions/"DevIdp"+"Follow" HttpClient wiring
            // as RbacProjectionWorkerHttpSqliteTests.cs's own ClassInit --
            // required for RbacProjectionWorker.CatchUpOnceAsync to be
            // driven directly below, well after both factories are fully
            // built (the self-referential "DevIdp" client hazard both
            // files already document).
            builder.UseSetting("Rbac:Client:ClientId", "devidp-rbac-follower-client");
            builder.UseSetting("Rbac:Client:ClientSecret", "devidp-rbac-follower-client-secret");
            builder.UseSetting("Rbac:Client:Scope", "events:follow");
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri("http://localhost/"))
                    .ConfigurePrimaryHttpMessageHandler(() => _devIdpFactory.Server.CreateHandler());
                services.AddHttpClient("Follow", c => c.BaseAddress = new Uri("http://localhost/"))
                    .ConfigurePrimaryHttpMessageHandler(() => _hostFactory.Server.CreateHandler());
            });
        });
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

    private static RbacProjectionWorker CreateWorker()
    {
        var followClient = _devIdpFactory.Services.GetRequiredService<FollowClient>();
        var rbacOptions = _devIdpFactory.Services.GetRequiredService<IOptions<RbacProjectionOptions>>();
        var scopeFactory = _devIdpFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var logger = _devIdpFactory.Services.GetRequiredService<ILogger<RbacProjectionWorker>>();
        return new RbacProjectionWorker(scopeFactory, followClient, rbacOptions, logger);
    }

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

    private static async Task RevokeDelegationAsync(string appId, Guid grantRef)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/ucan/delegations/{grantRef}/revoke")
        {
            Content = JsonContent.Create(new { appId }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task RevokingADelegationBlocksASubsequentExchangeOfTheSameDelegation()
    {
        const string appId = "ucan-revocation-demo-1";

        var (granterToken, _) = await GetSubjectTokenAsync("clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");
        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");

        var delegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", null)],
            TimeSpan.FromMinutes(5), granterToken);

        // Extract this delegation's own GrantRef ("jti") the same way a
        // real granter application would after UcanDelegation.Create
        // returns -- decoding its own just-signed JWT, not a value handed
        // in separately.
        var grantRef = Guid.Parse(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(delegation).GetClaim("jti").Value);

        var beforeRevoke = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, beforeRevoke.StatusCode, await beforeRevoke.Content.ReadAsStringAsync());

        await RevokeDelegationAsync(appId, grantRef);

        var worker = CreateWorker();
        var (outcome, consumed) = await worker.CatchUpOnceAsync(appId, "UcanDelegationRevoked", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.AreEqual(CatchUpOutcome.Completed, outcome);
        Assert.AreEqual(1, consumed, "expected the real Follow subscription to deliver exactly the one UcanDelegationRevoked event just published");

        var afterRevoke = await ExchangeAsync(delegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, afterRevoke.StatusCode, await afterRevoke.Content.ReadAsStringAsync());
        var body = await afterRevoke.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.AreEqual("invalid_grant", body.GetProperty("error").GetString());
        Assert.AreEqual("delegation has been revoked", body.GetProperty("error_description").GetString());
    }

    [TestMethod]
    public async Task ADelegationNeverRevokedStillExchangesSuccessfullyAfterAFoldPassRunsForAnUnrelatedGrantRef()
    {
        // Confirms the revocation check is genuinely keyed by GrantRef, not
        // a blanket "any revocation event exists for this AppId" check --
        // a real, previously-untested false-positive risk given
        // RevocationService.IsRevokedAsync's own AnyAsync(r => r.GrantRef
        // == grantRef) shape would silently pass even a wrong query if
        // this weren't exercised.
        const string appId = "ucan-revocation-demo-2";

        var (granterToken, _) = await GetSubjectTokenAsync("clinician-spa-client", "clinician-spa-client-secret", "");
        var granterKey = DevIdpSeeder.GetClientKeyPair("clinician-spa-client");
        var granteeKey = DevIdpSeeder.GetClientKeyPair("colleague-client");

        var untouchedDelegation = UcanDelegation.Create(
            granterKey, "clinician-spa-client", "colleague-client", appId,
            [new DelegatedCapability("clearance:phi", null)],
            TimeSpan.FromMinutes(5), granterToken);

        await RevokeDelegationAsync(appId, Guid.NewGuid()); // an unrelated GrantRef, never delegated
        var worker = CreateWorker();
        await worker.CatchUpOnceAsync(appId, "UcanDelegationRevoked", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);

        var response = await ExchangeAsync(untouchedDelegation, appId, "colleague-client", "colleague-client-secret", granteeKey);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
