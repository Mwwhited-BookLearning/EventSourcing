extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Flows;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Projections.Host;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Vitals;

namespace EventStore.IntegrationTests;

// ADR-101 -- the same real, end-to-end bar ProjectionsSqliteTests already
// established for OrderSummaryProjection (real HTTP registration/publish
// through a genuine EventStore.Host.Sqlite TestServer, a real DPoP-bound
// DevIdp-issued token, ProjectionHost driven via its own bounded
// CatchUpOnceAsync), applied here to FlowProjection/PendingTask instead --
// using the REAL embedded .puml (VitalsWorkflowBFlow.Build(), the exact
// FlowDefinition the live Samples.Vitals.Flows worker registers), not a
// synthetic flow. VitalsWorkflowB's own schemas are registered via the
// real SchemaRegistryService resolved from the Host's own composition root
// (SanctionsScreeningExtensibilityHttpSqliteTests' own established
// pattern for "stands in for a worker/detector" -- genuinely real, same
// database, just skipping the registry's own HTTP wire hop) rather than
// hand-duplicating VitalsWorkflowB.cs's schema JSON a second time here.
[TestClass]
public class PendingTaskProjectionSqliteTests
{
    private static string _dbPath = default!;
    private static string _pendingTasksDbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-pending-task-write-{Guid.NewGuid():N}.db");
        _pendingTasksDbPath = Path.Combine(Path.GetTempPath(), $"pending-tasks-{Guid.NewGuid():N}.db");

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

        using (var scope = _hostFactory.Services.CreateScope())
            await VitalsWorkflowB.RegisterAsync(scope.ServiceProvider.GetRequiredService<SchemaRegistryService>());

        using var pendingTasksDb = CreatePendingTasksDb();
        await pendingTasksDb.Database.MigrateAsync();
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
        if (File.Exists(_pendingTasksDbPath))
            File.Delete(_pendingTasksDbPath);
    }

    private static PendingTasksDbContext CreatePendingTasksDb()
    {
        var options = new DbContextOptionsBuilder<PendingTasksDbContext>()
            .UseSqlite($"Data Source={_pendingTasksDbPath}")
            .Options;
        return new PendingTasksDbContext(options);
    }

    private static ProjectionHost<PendingTask> BuildHost(FlowProjection projection)
    {
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient>
        {
            ["Follow"] = _hostClient,
            ["DevIdp"] = _devIdpClient,
        });
        var followClientOptions = Options.Create(new FollowClientOptions
        {
            ClientId = "projections-client",
            ClientSecret = "projections-client-secret",
            Scope = "events:follow",
        });
        var followClient = new FollowClient(httpClientFactory, followClientOptions);
        var hostOptions = Options.Create(new ProjectionHostOptions { AppId = VitalsWorkflowB.AppId });

        var services = new ServiceCollection();
        services.AddFlowEngine($"Data Source={_pendingTasksDbPath}");
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ProjectionHost<PendingTask>(scopeFactory, projection, followClient, hostOptions, NullLogger<ProjectionHost<PendingTask>>.Instance);
    }

    private static async Task RunCatchUpForAllEventTypesAsync(ProjectionHost<PendingTask> host, FlowProjection projection)
    {
        foreach (var eventType in projection.EventTypes)
            await host.CatchUpOnceAsync(eventType, int.MaxValue, TimeSpan.FromMilliseconds(500), CancellationToken.None);
    }

    private static async Task<Guid> PublishAdverseEventReportedAsync(string aeId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var payload = JsonSerializer.Serialize(new
        {
            AeId = aeId,
            SubjectId = "subject-http-1",
            SiteId = "site-1",
            Description = "Real HTTP-driven flow-engine regression test",
            Severity = "moderate",
            SeriousAdverseEvent = false,
            CausalityAssessment = "unrelated",
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/AdverseEventReported")
        {
            Content = JsonContent.Create(new { appId = VitalsWorkflowB.AppId, schemaVersion = 1, payload }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    // vitals-pi-client, step-up satisfied (urn:trial:step-up, ADR-066) --
    // the same real identity/mechanism VitalsPrincipalInvestigatorQueuePlaybookTests
    // already drives through the browser, exercised here over plain HTTP.
    private static async Task<HttpStatusCode> PublishAuthorityDecisionAsync(Guid targetEventId, string decision)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "vitals-pi-client", "vitals-pi-client-secret", "events:publish", acr: "urn:trial:step-up");
        var payload = JsonSerializer.Serialize(new
        {
            targetEventId = targetEventId.ToString(),
            decision,
            decidingActorId = "pi-1",
            reason = "reviewed via PendingTaskProjectionSqliteTests",
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/authorityDecision")
        {
            Content = JsonContent.Create(new { appId = VitalsWorkflowB.AppId, schemaVersion = 1, payload, meaning = "Reviewed and signed off" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return response.StatusCode;
    }

    // ONE test method sharing ONE host/projection instance across both
    // scenarios -- ProjectionsSqliteTests.AllProjectionScenarios' own
    // established shape, not incidental: a second, independently-built
    // ProjectionHost<PendingTask> against the SAME physical checkpoint
    // database hits a real UNIQUE-constraint violation on Checkpoints.
    // ProjectionName the moment it tries to insert a fresh checkpoint row
    // for a flow name a prior host instance already checkpointed --
    // confirmed by actually splitting this into two [TestMethod]s first
    // and hitting exactly that SQLite error, not assumed.
    [TestMethod]
    public async Task AllPendingTaskScenarios()
    {
        var projection = new FlowProjection(VitalsWorkflowBFlow.Build());
        var host = BuildHost(projection);

        await ARealAdverseEventReportedPublishedOverHttpCreatesAnOpenPendingTaskRowViaTheRealEmbeddedPuml(host, projection);
        await AStepUpSatisfyingAuthorityDecisionOverHttpResolvesAndDeletesThePendingTaskRow(host, projection);
    }

    private static async Task ARealAdverseEventReportedPublishedOverHttpCreatesAnOpenPendingTaskRowViaTheRealEmbeddedPuml(ProjectionHost<PendingTask> host, FlowProjection projection)
    {
        var eventId = await PublishAdverseEventReportedAsync("ae-http-1");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var db = CreatePendingTasksDb();
        var row = await db.PendingTasks.AsNoTracking().SingleAsync(t => t.Key == eventId.ToString());
        Assert.AreEqual("vitals-workflow-b-adverse-event-review", row.FlowName);
        Assert.AreEqual("PI must review and sign off on the adverse event", row.Description);
        Assert.AreEqual("review:ae", row.RequiredClaim);
        Assert.AreEqual(VitalsWorkflowB.AppId, row.AppId);
        Assert.AreEqual("ae-http-1", row.EntityId);
        Assert.AreEqual(eventId.ToString(), row.TriggeringEventId);
    }

    private static async Task AStepUpSatisfyingAuthorityDecisionOverHttpResolvesAndDeletesThePendingTaskRow(ProjectionHost<PendingTask> host, FlowProjection projection)
    {
        var eventId = await PublishAdverseEventReportedAsync("ae-http-2");
        await RunCatchUpForAllEventTypesAsync(host, projection);

        using (var db = CreatePendingTasksDb())
            Assert.IsTrue(await db.PendingTasks.AsNoTracking().AnyAsync(t => t.Key == eventId.ToString()), "the task must exist before the decision resolves it");

        var decisionStatus = await PublishAuthorityDecisionAsync(eventId, "accepted");
        Assert.AreEqual(HttpStatusCode.Accepted, decisionStatus);

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var afterDb = CreatePendingTasksDb();
        Assert.IsFalse(await afterDb.PendingTasks.AsNoTracking().AnyAsync(t => t.Key == eventId.ToString()), "a resolved task's row must be deleted, not left open");
    }
}
