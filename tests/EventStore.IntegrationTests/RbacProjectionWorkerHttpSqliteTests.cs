extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Projections.Host;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RbacProjectionWorker = DevIdpAssembly::EventStore.DevIdp.RbacProjectionWorker;
using RbacProjectionOptions = DevIdpAssembly::EventStore.DevIdp.RbacProjectionOptions;

namespace EventStore.IntegrationTests;

// TODO.md's own "RbacProjectionWorker's live, cross-process Follow
// subscription is not exercised end-to-end by any test" gap --
// DelegatedGrantsRbacFederationHttpSqliteTests.cs verifies the Host's
// write path and applies the SAME fold TARGET methods
// (RoleService/TrustRootService) directly, standing in for the worker,
// specifically because running the real worker inside a test process hit
// a genuine WebApplicationFactory hazard (BackgroundService.StartAsync
// invokes ExecuteAsync synchronously, inline, and the worker's own
// self-referential "DevIdp" HttpClient recursed into a
// WebApplicationFactory still being built one level up the call stack).
//
// This file closes that gap the way TODO.md's own suggested fix names:
// RbacProjectionWorker.CatchUpOnceAsync (extracted this same pass,
// mirroring ProjectionHost<TReadModel>'s identical shape) is called
// DIRECTLY, post-ClassInit -- both WebApplicationFactory instances are
// already fully built by the time this ever runs, so the self-reference
// hazard never has a chance to occur; BackgroundService.StartAsync/
// ExecuteAsync are never invoked at all. This exercises the REAL Follow
// subscription (FollowClient.TailAsync against the Host's own real
// /follow/{eventType} SSE endpoint, real DPoP-bound client_credentials
// token acquisition against DevIdp itself) and the real event-dispatch-
// by-type logic inside ApplyAsync -- not just the fold target methods
// the existing test already covers directly.
//
// [DoNotParallelize]: every test method here requests a token for the
// SAME seeded "colleague-client" identity, using DevIdpSeeder's own
// FIXED, shared DpopKeyPair for that client -- [assembly: Parallelize
// (Scope = ExecutionScope.MethodLevel)] (MSTestSettings.cs) running
// these concurrently produced a real, confirmed intermittent failure
// (roughly 1 run in 4) with no logic bug behind it: re-running the
// exact same failing test alone always passed. Consistent with the
// same class of interference task #113's ticket-rotation tests already
// found and fixed the same way (a shared per-client credential/key
// racing across concurrently-scheduled test methods), not a defect in
// RbacProjectionWorker.CatchUpOnceAsync itself.
[TestClass]
[DoNotParallelize]
public class RbacProjectionWorkerHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-rbac-worker-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>().WithWebHostBuilder(builder =>
        {
            // FollowClientOptions -- matches devidp-rbac-follower-client's
            // own seeded identity (DevIdpSeeder), the same client
            // Program.cs's own production Rbac:Client config would name
            // for the real worker.
            builder.UseSetting("Rbac:Client:ClientId", "devidp-rbac-follower-client");
            builder.UseSetting("Rbac:Client:ClientSecret", "devidp-rbac-follower-client-secret");
            builder.UseSetting("Rbac:Client:Scope", "events:follow");
            builder.ConfigureServices(services =>
            {
                // "DevIdp" -- self-referential by design (FollowClient's
                // own GetAccessTokenAsync calls back into this SAME
                // process's own /connect/token). Safe here specifically
                // because this handler factory is a DEFERRED lambda, only
                // invoked when CatchUpOnceAsync actually runs, long after
                // ClassInit has fully finished building _devIdpFactory --
                // never during the factory's own construction, which is
                // exactly the hazard this test exists to avoid hitting.
                // An explicit BaseAddress matching WebApplicationFactory.
                // CreateClient()'s own default ("http://localhost/") is
                // required here -- DpopKeyPair.CreateProof's htu is derived
                // from THIS client's own BaseAddress (FollowClient.
                // AttachAuth), and the resource server's own DPoP
                // validation middleware rejects a proof whose htu doesn't
                // match its own computed expected value byte-for-byte;
                // found only by running this (a real, otherwise-silent
                // 401 with no WWW-Authenticate detail at all -- confirmed
                // via a diagnostic call using AuthScenarioAssertions'
                // already-working AttachAuth/GetTokenAsync helpers against
                // the identical endpoint/credentials, which succeeded,
                // isolating the gap to specifically this BaseAddress).
                services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri("http://localhost/"))
                    .ConfigurePrimaryHttpMessageHandler(() => _devIdpFactory.Server.CreateHandler());
                // "Follow" -- the real Host TestServer, built below.
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

    private static async Task PutRoleDefinitionAsync(string appId, string roleName, string permission)
    {
        var response = await _devIdpClient.PutAsync("/oauth/roles",
            JsonContent.Create(new { appId, roleName, permissions = new[] { permission } }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CatchUpOnceAsyncFoldsARealRoleGrantedEventThroughTheLiveFollowSubscription()
    {
        const string appId = "rbac-worker-demo-1";
        const string actorId = "colleague-client";
        await PutRoleDefinitionAsync(appId, "role-worker-1", "scoped:permWorker1");
        await GrantRoleAsync(appId, actorId, "role-worker-1");

        var worker = CreateWorker();
        var consumed = await worker.CatchUpOnceAsync(appId, "RoleGranted", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.AreEqual(1, consumed, "expected the real Follow subscription to deliver exactly the one RoleGranted event just published");

        var (token, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decoded = new JsonWebToken(token);
        Assert.AreEqual("permWorker1", decoded.GetClaim("scoped").Value, "the worker's own real fold (not a stand-in call) must have applied RoleGranted via RoleService.AssignRoleAsync");
    }

    [TestMethod]
    public async Task CatchUpOnceAsyncFoldsARealRoleRevokedEventThroughTheLiveFollowSubscription()
    {
        const string appId = "rbac-worker-demo-2";
        const string actorId = "colleague-client";
        await PutRoleDefinitionAsync(appId, "role-worker-2", "scoped:permWorker2");
        await GrantRoleAsync(appId, actorId, "role-worker-2");

        var worker = CreateWorker();
        var grantedConsumed = await worker.CatchUpOnceAsync(appId, "RoleGranted", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.AreEqual(1, grantedConsumed);

        await RevokeRoleAsync(appId, actorId, "role-worker-2");
        var revokedConsumed = await worker.CatchUpOnceAsync(appId, "RoleRevoked", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.AreEqual(1, revokedConsumed, "expected the real Follow subscription to deliver exactly the one RoleRevoked event just published");

        var (token, _) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "colleague-client", "colleague-client-secret", "", appId);
        var decoded = new JsonWebToken(token);
        Assert.IsFalse(decoded.TryGetClaim("scoped", out _), "the worker's own real fold of RoleRevoked must have removed the permission RoleGranted's own fold added");
    }

    [TestMethod]
    public async Task CatchUpOnceAsyncStopsAtItsOwnIdleTimeoutRatherThanHangingForMaxEventsToConsume()
    {
        const string appId = "rbac-worker-demo-3";
        const string actorId = "colleague-client";
        // Exactly ONE PermissionGranted event exists for this AppId --
        // asking for up to 5 with a short idle timeout must return after
        // that one, once the timeout elapses with no second event ever
        // arriving, rather than hanging until maxEventsToConsume is
        // actually reached. A genuinely NEVER-registered event type
        // 404s instead of idling (RbacProjectionWorker.TailForeverAsync's
        // own reconnect loop is what's designed to absorb that -- not
        // this bounded, single-pass method), so this needs a real,
        // already-registered type to observe the idle-timeout path at
        // all -- confirmed by actually running this against a genuinely
        // unregistered type first and getting a 404, not a graceful zero.
        await GrantPermissionAsync(appId, actorId, "direct:permWorker3");

        var worker = CreateWorker();
        var consumed = await worker.CatchUpOnceAsync(appId, "PermissionGranted", maxEventsToConsume: 5, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.AreEqual(1, consumed, "expected the idle timeout to stop consumption after the one real event, not hang waiting for a 5th that never arrives");
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
}
