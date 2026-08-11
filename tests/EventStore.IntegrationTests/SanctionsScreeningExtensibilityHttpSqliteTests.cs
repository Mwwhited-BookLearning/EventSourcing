extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Domain.EventLog;
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
using RoleService = DevIdpAssembly::EventStore.DevIdp.RoleService;

namespace EventStore.IntegrationTests;

// "Sanctions/Watchlist Screening Extensibility Seam" (docs/08-build-plan.md,
// ADR-079) -- the first domain-scoped (non-core) extension point in this
// design. ISanctionsScreeningProvider/ScreeningResult and their one concrete
// backend are defined and keyed-DI-registered ENTIRELY inside this test
// file's own ConfigureServices block, standing in for the KYC/Meridian
// application's own composition root -- the same WebApplicationFactory
// simulation TenantFederationHttpSqliteTests already established for item
// 37's "a hosting team's own Program.cs" -- zero footprint in any core
// EventStore.* project, exactly as ADR-079's own Decision text requires.
// The screening hit's actual gating -- landing pending_review regardless of
// confidence, resolved only by a compliance officer's ordinary, RBAC-gated
// authorityDecision -- reuses "Non-Authoritative Capture" (item 18) and
// "Delegated Grants, RBAC, Federated Claims..." (item 23) completely
// unchanged; no new framework mechanism is introduced here, matching
// docs/domains/digital-identity-kyc/features/periodic-screening-and-sar-
// escalation.md's own manual-decision flow this seam wraps a signal around.
[TestClass]
public class SanctionsScreeningExtensibilityHttpSqliteTests
{
    // ADR-079's own interface shape -- deliberately NOT declared in any
    // EventStore.Abstractions/core project; this IS "the KYC application's
    // own extension point" (ADR-059's pattern), not a framework-predefined one.
    private interface ISanctionsScreeningProvider
    {
        Task<ScreeningResult> ScreenAsync(IdentityClaim claim, CancellationToken ct = default);
    }

    private sealed record IdentityClaim(string ApplicantId, string FullName);
    private sealed record ScreeningResult(bool MatchFound, double? MatchConfidence, string? MatchedName, string? MatchedListEntryId);

    // A fake OFAC-SDN-style backend -- two hardcoded watchlist entries at
    // different confidence levels, purely to prove the seam's wiring never
    // trusts a hit regardless of confidence, not a real sanctions-list
    // integration.
    private sealed class TestOfacScreeningProvider : ISanctionsScreeningProvider
    {
        public Task<ScreeningResult> ScreenAsync(IdentityClaim claim, CancellationToken ct = default) => Task.FromResult(claim.FullName switch
        {
            "Jane Smith" => new ScreeningResult(true, 0.87, "Jane Smith", "SDN-44291"),
            "Robert Lowconf" => new ScreeningResult(true, 0.52, "Robert Lowconf", "SDN-19004"),
            _ => new ScreeningResult(false, null, null, null),
        });
    }

    private const string ScreeningProviderKey = "Ofac";
    private const string AppId = "kyc-screening-demo";

    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-sanctions-screening-http-{Guid.NewGuid():N}.db");
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

        // This ConfigureServices block IS "the KYC/Meridian application's
        // own composition root" for the purposes of this test -- registering
        // a keyed ISanctionsScreeningProvider that exists ONLY here, never
        // in any core EventStore.* project (ADR-079's central claim).
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
                services.AddKeyedScoped<ISanctionsScreeningProvider, TestOfacScreeningProvider>(ScreeningProviderKey);
            });
        });
        _hostClient = _hostFactory.CreateClient();

        await RegisterEventTypesAsync();
        await RegisterComplianceOfficerRoleAsync();
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

    private static async Task RegisterEventTypesAsync()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");

        using var screeningRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/SanctionsScreeningPerformed")
        {
            Content = JsonContent.Create(new
            {
                appId = AppId,
                jsonSchema = """{ "type": "object", "properties": { "ApplicantId": { "type": "string" }, "ScreeningDate": { "type": "string" }, "MatchFound": { "type": "boolean" }, "MatchConfidence": { "type": "number" }, "MatchedName": { "type": "string" }, "MatchedListEntryId": { "type": "string" } }, "required": ["ApplicantId", "ScreeningDate", "MatchFound"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.ApplicantId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(screeningRequest, _hostClient, token, key);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(screeningRequest)).StatusCode);

        // RequiredClaims here is the SAME generic "authorityDecision" gating
        // mechanism customer-onboarding/periodic-screening's own domain doc
        // uses (ADR-050) -- "identity:aml-review" is the only claim able to
        // resolve a screening hit in this demo, deliberately narrower than
        // that doc's own OR-evaluated ["identity:review", "identity:aml-review"]
        // since this seam has no separate "identity:review" reviewer persona.
        using var decisionRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/authorityDecision")
        {
            Content = JsonContent.Create(new
            {
                appId = AppId,
                jsonSchema = """{ "type": "object", "properties": { "targetEventId": { "type": "string" }, "decision": { "type": "string" }, "decidingActorId": { "type": "string" }, "reason": { "type": "string" } }, "required": ["targetEventId", "decision", "decidingActorId"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.targetEventId",
                parentValidationMode = "Permissive",
                requiredClaims = new[] { new { direction = "Publish", claim = "identity:aml-review" } },
            }),
        };
        AuthScenarioAssertions.AttachAuth(decisionRequest, _hostClient, token, key);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(decisionRequest)).StatusCode);
    }

    // ADR-046 -- a "ComplianceOfficer" role bundling "identity:aml-review",
    // the same RBAC mechanism "Delegated Grants, RBAC..." (item 23) already
    // built and DelegatedGrantsRbacFederationHttpSqliteTests already
    // exercises; this seam adds no new authorization mechanism of its own.
    // Granted to "publisher-client" itself (which already holds the
    // events:publish scope the /publish endpoint separately requires) rather
    // than a second seeded client, so this compliance officer persona can
    // actually call /publish/authorityDecision, not just carry the claim.
    private static async Task RegisterComplianceOfficerRoleAsync()
    {
        var putRoleResponse = await _devIdpClient.PutAsync("/oauth/roles",
            JsonContent.Create(new { appId = AppId, roleName = "ComplianceOfficer", permissions = new[] { "identity:aml-review" } }));
        Assert.AreEqual(HttpStatusCode.Created, putRoleResponse.StatusCode);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var assignRequest = new HttpRequestMessage(HttpMethod.Post, "/rbac/roles/ComplianceOfficer/assignments")
        {
            Content = JsonContent.Create(new { appId = AppId, actorId = "publisher-client" }),
        };
        AuthScenarioAssertions.AttachAuth(assignRequest, _hostClient, token, key);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(assignRequest)).StatusCode);

        // Stands in for DevIdp's own live RbacProjectionWorker Follow fold --
        // the same "apply the target fold method directly" posture
        // DelegatedGrantsRbacFederationHttpSqliteTests's own comment
        // explains (a WebApplicationFactory hazard, not designed around
        // again here).
        using var scope = _devIdpFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>().AssignRoleAsync("publisher-client", AppId, "ComplianceOfficer");
    }

    // Stands in for PeriodicScreeningWorker (docs/domains/digital-identity-
    // kyc/features/periodic-screening-and-sar-escalation.md) -- an ordinary
    // domain-level scheduled job, not a framework concept. Resolves the
    // seam's provider from the KYC application's OWN composition root
    // (never a core EventStore.* service) and publishes exactly like any
    // other automated detector (ADR-079's own Invocation point).
    // Returns the parsed fields the caller needs rather than the raw
    // HttpResponseMessage -- its Content stream can only be read once,
    // and every call site here needs both the status/authorityStatus AND
    // the correlationId.
    private static async Task<(HttpStatusCode Status, Guid EventId, string? AuthorityStatus)> PerformScreeningAsync(string applicantId, string fullName)
    {
        using var scope = _hostFactory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredKeyedService<ISanctionsScreeningProvider>(ScreeningProviderKey);
        var result = await provider.ScreenAsync(new IdentityClaim(applicantId, fullName));

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var payload = JsonSerializer.Serialize(new
        {
            ApplicantId = applicantId,
            ScreeningDate = "2026-08-10",
            MatchFound = result.MatchFound,
            MatchConfidence = result.MatchConfidence,
            MatchedName = result.MatchedName,
            MatchedListEntryId = result.MatchedListEntryId,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/SanctionsScreeningPerformed")
        {
            Content = JsonContent.Create(new
            {
                appId = AppId,
                schemaVersion = 1,
                payload,
                reviewPending = result.MatchFound, // ADR-079 -- a hit is NEVER trusted by default, regardless of confidence
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.Accepted)
            return (response.StatusCode, Guid.Empty, null);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body.GetProperty("correlationId").GetGuid(), body.GetProperty("authorityStatus").GetString());
    }

    private static async Task<HttpStatusCode> PublishAuthorityDecisionAsync(Guid targetEventId, string decision, string decidingActorId, bool asComplianceOfficer)
    {
        var (token, key) = asComplianceOfficer
            ? await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish", AppId)
            : await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/authorityDecision")
        {
            Content = JsonContent.Create(new
            {
                appId = AppId,
                schemaVersion = 1,
                payload = JsonSerializer.Serialize(new { targetEventId = targetEventId.ToString(), decision, decidingActorId }),
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return response.StatusCode;
    }

    // RouterWorker's own 200ms poll cycle occasionally needs more than a
    // fixed 500ms wait under this suite's heavier full-run parallel load
    // (found running the full regression suite alongside 82 other tests,
    // never in isolation) -- the same load-induced flake class TODO.md
    // already tracks for other HTTP test files. Polls the actual
    // condition instead of assuming one fixed wait is always enough.
    private static async Task<StoredEvent> WaitForAuthorityStatusAsync(EventStoreContext db, Guid eventId, string expectedStatus)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId);
            if (target.AuthorityStatus == expectedStatus || DateTime.UtcNow >= deadline)
                return target;
            await Task.Delay(150);
        }
    }

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

    [TestMethod]
    public void TheProviderIsResolvedFromTheApplicationsOwnCompositionRootNotAnyCoreEventStoreProject()
    {
        using var scope = _hostFactory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetKeyedService<ISanctionsScreeningProvider>(ScreeningProviderKey);
        Assert.IsNotNull(provider, "the KYC application's own composition root -- this test's ConfigureServices block -- registered this, not any core EventStore.* project");
        Assert.IsInstanceOfType<TestOfacScreeningProvider>(provider);
    }

    [TestMethod]
    public async Task ScreeningAnIdentityWithNoMatchIsAcceptedImmediatelyLikeAnyOrdinaryPublish()
    {
        var (status, _, authorityStatus) = await PerformScreeningAsync("applicant-clear-1", "Nobody Notable");
        Assert.AreEqual(HttpStatusCode.Accepted, status);
        Assert.AreEqual("accepted", authorityStatus);
    }

    [TestMethod]
    public async Task ScreeningAnIdentityThatMatchesLandsPendingReviewRegardlessOfMatchConfidence()
    {
        var (highConfidenceStatus, _, highConfidenceAuthorityStatus) = await PerformScreeningAsync("applicant-1001", "Jane Smith");
        Assert.AreEqual(HttpStatusCode.Accepted, highConfidenceStatus);
        Assert.AreEqual("pending_review", highConfidenceAuthorityStatus, "a 0.87-confidence hit is still never auto-accepted");

        var (lowConfidenceStatus, _, lowConfidenceAuthorityStatus) = await PerformScreeningAsync("applicant-2044", "Robert Lowconf");
        Assert.AreEqual(HttpStatusCode.Accepted, lowConfidenceStatus);
        Assert.AreEqual("pending_review", lowConfidenceAuthorityStatus, "even a 0.52-confidence hit is never auto-accepted -- ADR-079 gates on MatchFound alone, not confidence");
    }

    [TestMethod]
    public async Task ACallerWithoutTheAmlReviewClaimCannotResolveTheFlaggedMatch()
    {
        var (screenStatus, eventId, screenAuthorityStatus) = await PerformScreeningAsync("applicant-3055", "Jane Smith");
        Assert.AreEqual(HttpStatusCode.Accepted, screenStatus);
        Assert.AreEqual("pending_review", screenAuthorityStatus);

        var decisionStatus = await PublishAuthorityDecisionAsync(eventId, "accepted", "clerk-1", asComplianceOfficer: false);
        Assert.AreEqual(HttpStatusCode.Forbidden, decisionStatus, "no identity:aml-review claim was requested (no app_id) -- ordinary RBAC gating, unchanged by this seam");

        await using var db = OpenDb();
        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId);
        Assert.AreEqual("pending_review", target.AuthorityStatus, "a rejected-at-the-gate decision attempt never touches the target's own AuthorityStatus");
    }

    [TestMethod]
    public async Task AComplianceOfficersAuthorityDecisionResolvesTheHitToAcceptedTheProviderNeverDecidesItself()
    {
        var (screenStatus, eventId, screenAuthorityStatus) = await PerformScreeningAsync("applicant-4066", "Jane Smith");
        Assert.AreEqual(HttpStatusCode.Accepted, screenStatus);
        Assert.AreEqual("pending_review", screenAuthorityStatus,
            "the provider only ever supplies the signal that trips reviewPending -- it never sets AuthorityStatus to accepted itself");

        var decisionStatus = await PublishAuthorityDecisionAsync(eventId, "accepted", "publisher-client", asComplianceOfficer: true);
        Assert.AreEqual(HttpStatusCode.Accepted, decisionStatus);

        await using var db = OpenDb();
        var target = await WaitForAuthorityStatusAsync(db, eventId, "accepted");
        Assert.AreEqual("accepted", target.AuthorityStatus, "the compliance officer's own authorityDecision -- not the provider -- is what actually resolves the hit");
    }

    [TestMethod]
    public async Task AComplianceOfficerCanAlsoClearAFlaggedMatchAsAFalsePositive()
    {
        var (screenStatus, eventId, screenAuthorityStatus) = await PerformScreeningAsync("applicant-5077", "Jane Smith");
        Assert.AreEqual(HttpStatusCode.Accepted, screenStatus);
        Assert.AreEqual("pending_review", screenAuthorityStatus);

        var decisionStatus = await PublishAuthorityDecisionAsync(eventId, "rejected", "publisher-client", asComplianceOfficer: true);
        Assert.AreEqual(HttpStatusCode.Accepted, decisionStatus);

        await using var db = OpenDb();
        var target = await WaitForAuthorityStatusAsync(db, eventId, "rejected");
        Assert.AreEqual("rejected", target.AuthorityStatus, "a false-positive clearance resolves to rejected, never leaving the hit stuck pending_review");
    }
}
