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

// TODO.md's demo-identity gap, closed for real: "demo-dispatcher-client"
// (DevIdpSeeder.cs) could only ever publish to a throwaway, no-
// RequiredClaims schema (OfflineOutboxSyncPlaybookTests.cs's own comment:
// "no seeded HTTP client at all... holds" a real domain event type's own
// RequiredClaims). ADR-105 decided the fix's shape: the demo identity gets
// a generalized role as a JWT claim, expanded per-application into real
// permissions via either an RFC 8693 exchange or middleware.
//
// This closes it via the EXISTING, already-built mechanism -- no new
// middleware/endpoint, per this project's own "search for prior art before
// inventing" rule (.claude/protocols/verify-before-citing.md):
// EventStore.DevIdp's RoleService/Role/RoleAssignment (ADR-046) already
// bundle an AppId-scoped RoleName into a permission set, and
// Program.cs's own /connect/token already has an opt-in "app_id" form
// parameter (used today by DelegatedGrantsRbacFederationHttpSqliteTests.cs/
// RbacProjectionWorkerHttpSqliteTests.cs's "colleague-client"/role-a
// scenarios) that expands whatever role(s) the caller holds for that AppId
// into real claims on the SAME token, in one round trip -- functionally
// exactly ADR-105's "per-application expansion step," just already wired
// into the primary token call instead of a second exchange hop. The
// generalized part is the ROLE NAME ("demo") itself: nothing stops the
// identical RoleName being independently defined (with a different
// Permissions bundle) under a different AppId for Meridian, the same
// "one role name, per-application meaning" shape ADR-105 describes -- only
// Vitals is proven here (TODO.md's own "at least one domain" bar), Meridian
// is structurally identical and left to a future pass if/when needed.
//
// Role GRANTING still goes through the real, hash-chained RoleGranted
// event + RbacProjectionWorker fold (ADR-067) -- ADR-105 says explicitly
// this decision "reuses this design's existing, already-config-driven
// pipeline for the role/permission-grant mechanism itself," not a
// shortcut around it. Role DEFINITION (PUT /oauth/roles, what "demo"
// bundles) stays the genuine DevIdp-internal, non-event-sourced
// configuration ADR-067's own Decision already scopes it as.
[TestClass]
[DoNotParallelize]
public class DemoIdentityRoleExpansionHttpSqliteTests
{
    private const string VitalsAppId = "trial1"; // Samples.Vitals.VitalsWorkflowA.AppId, the real Vitals trial AppId
    private const string DemoRoleName = "demo";
    private const string PatientEnrollClaim = "patient:enroll"; // VitalsWorkflowA's real PatientScreened RequiredClaims entry

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-demo-role-expansion-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        // Same "DevIdp"/"Follow" named-HttpClient wiring as
        // RbacProjectionWorkerHttpSqliteTests.cs -- needed to construct and
        // drive a real RbacProjectionWorker.CatchUpOnceAsync against both
        // TestServers, not a stand-in call to RoleService directly.
        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>().WithWebHostBuilder(builder =>
        {
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

    // The real PatientScreened schema (VitalsWorkflowA.cs's own
    // PatientScreenedSchema/RequiredClaims, reproduced verbatim over raw
    // HTTP the same way VitalsWorkflowBSecondaryOpinionHttpSqliteTests.cs's
    // own RegisterAdverseEventReportedAsync does for its event type) --
    // this test proves demo-dispatcher-client against the SAME claim gate
    // a real Vitals workflow enforces, not a look-alike.
    private static async Task RegisterPatientScreenedAsync()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/PatientScreened")
        {
            Content = JsonContent.Create(new
            {
                appId = VitalsAppId,
                jsonSchema = """{ "type": "object", "properties": { "SubjectId": { "type": "string" }, "SiteId": { "type": "string" }, "EligibilityStatus": { "type": "string" } }, "required": ["SubjectId", "SiteId", "EligibilityStatus"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.SubjectId",
                entityType = "Patient",
                requiredClaims = new[] { new { direction = "Publish", claim = PatientEnrollClaim } },
            }),
        };
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

    [TestMethod]
    public async Task DemoDispatcherClientCanPublishARealVitalsPatientScreenedEventOnlyAfterItsGeneralizedDemoRoleIsGrantedAndFoldedForTrial1()
    {
        await RegisterPatientScreenedAsync();
        await PutRoleDefinitionAsync(VitalsAppId, DemoRoleName, PatientEnrollClaim);

        // --- Before: demo-dispatcher-client holds NO "demo" role grant yet
        // for trial1 -- a token requesting trial1's own expansion carries no
        // patient:enroll claim, and a real publish attempt is genuinely
        // Forbidden. This is the negative control proving the claim gate
        // (not something else) is what's actually being exercised below.
        var (tokenBefore, keyBefore) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "demo-dispatcher-client", "demo-dispatcher-client-secret", "events:publish", VitalsAppId);
        var decodedBefore = new JsonWebToken(tokenBefore);
        Assert.IsFalse(decodedBefore.TryGetClaim("patient", out _), "demo-dispatcher-client must NOT carry patient:enroll before the demo role is granted");

        using (var forbiddenRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/PatientScreened")
        {
            Content = JsonContent.Create(new
            {
                appId = VitalsAppId, schemaVersion = 1,
                payload = """{ "SubjectId": "subj-demo-before", "SiteId": "site-1", "EligibilityStatus": "Eligible" }""",
            }),
        })
        {
            AuthScenarioAssertions.AttachAuth(forbiddenRequest, _hostClient, tokenBefore, keyBefore);
            var forbiddenResponse = await _hostClient.SendAsync(forbiddenRequest);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode, "a real Vitals business event must reject demo-dispatcher-client before role expansion, proving this isn't an always-permissive schema");
        }

        // --- Grant the generalized "demo" role, real hash-chained
        // RoleGranted event, folded through the REAL RbacProjectionWorker
        // (not a stand-in call to RoleService) -- ADR-105's own "reuses the
        // existing grant-issuance pipeline" requirement.
        await GrantRoleAsync(VitalsAppId, "demo-dispatcher-client", DemoRoleName);
        var worker = CreateWorker();
        var (outcome, consumed) = await worker.CatchUpOnceAsync(VitalsAppId, "RoleGranted", maxEventsToConsume: 1, TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.AreEqual(CatchUpOutcome.Completed, outcome);
        Assert.AreEqual(1, consumed, "expected the real Follow subscription to deliver exactly the one RoleGranted event just published");

        // --- After: the SAME app_id-opt-in token request now carries the
        // real Vitals claim, expanded from the generalized "demo" role --
        // and a real publish of PatientScreened is genuinely Accepted.
        var (tokenAfter, keyAfter) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "demo-dispatcher-client", "demo-dispatcher-client-secret", "events:publish", VitalsAppId);
        var decodedAfter = new JsonWebToken(tokenAfter);
        Assert.AreEqual("enroll", decodedAfter.GetClaim("patient").Value, "the generalized \"demo\" role, expanded for trial1, must add the real patient:enroll claim");

        using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/PatientScreened")
        {
            Content = JsonContent.Create(new
            {
                appId = VitalsAppId, schemaVersion = 1,
                payload = """{ "SubjectId": "subj-demo-after", "SiteId": "site-1", "EligibilityStatus": "Eligible" }""",
            }),
        };
        AuthScenarioAssertions.AttachAuth(acceptedRequest, _hostClient, tokenAfter, keyAfter);
        var acceptedResponse = await _hostClient.SendAsync(acceptedRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, acceptedResponse.StatusCode, await acceptedResponse.Content.ReadAsStringAsync());
    }
}
