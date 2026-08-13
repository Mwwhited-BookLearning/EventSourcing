extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventStore.Domain.EventLog;
using EventStore.Inbox;
using EventStore.LineageExport;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Lineage Export & Bitemporal Playback" (docs/08-build-plan.md, ADR-068)
// -- a real end-to-end round trip through the actual GraphQL Gateway
// (exportLineage/playbackAsOf) and the two new REST endpoints
// (/lineage-exports/{id}, /lineage-imports), the same "prove the real
// transport, not just the service" posture TenantFederationHttpSqliteTests/
// DelegatedGrantsRbacFederationHttpSqliteTests already established. SQLite-
// only: the underlying IEventLineageQueryProvider traversal is already
// proven per-provider by LineageSqliteTests/PostgresTests/SqlServerTests
// (item 4/5) -- nothing this item adds has any new provider-specific SQL of
// its own, only EF LINQ and the same already-provider-agnostic traversal
// interface, matching the "auth/RBAC is provider-agnostic" reasoning this
// session already applied to several other HTTP test files.
[TestClass]
public class LineageExportHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-lineage-export-http-{Guid.NewGuid():N}.db");
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

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

    private static async Task RegisterEventTypeAsync(string appId, string name, string jsonSchema, string entityIdField, (string Direction, string Claim)[]? requiredClaims = null)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{name}")
        {
            Content = JsonContent.Create(new
            {
                appId, jsonSchema, filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField,
                requiredClaims = requiredClaims?.Select(c => new { direction = c.Direction, claim = c.Claim }).ToArray(),
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(request)).StatusCode);
    }

    private static async Task<Guid> PublishAsync(string appId, string eventType, string payload, List<Guid>? parentEventIds = null)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload, parentEventIds }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    // Retries up to ~10s (67 x 150ms, this session's own established
    // budget for a RouterWorker-tick race, see BatchPublishHttpSqliteTests)
    // rather than trusting each call site's own preceding Task.Delay(500)
    // to be enough -- reproduced directly, in full-suite runs, never in
    // isolation, twice, in two different ways:
    // ImportingABundleWithATamperedManifestHashIsRejectedBeforeAnyWrite
    // threw "requires an element of type 'Object', but the target element
    // has type 'Null'" because exportLineage was still null 500ms after
    // publish under heavy load; AnExportedFieldMaskedBecause...
    // got GraphQL's own "Unknown entityId." error instead, for the exact
    // same underlying reason -- LineageExportQueries.
    // GetExportLineageAsync's own CheckRootAsync queries the database
    // directly for a root event, and under load that query can run before
    // the publish it's racing against has actually landed. "Unknown
    // entityId." is retried here specifically because it is NOT
    // distinguishable, from this client's own perspective, from a
    // genuinely nonexistent entity (ExportingAnUnknownEntityIdIsRejected's
    // own scenario) -- only the retry BUDGET tells them apart: a
    // genuinely unknown entityId still returns the identical error after
    // the full ~10s retry window, a not-yet-landed one resolves well
    // before it. "Forbidden", by contrast
    // (ExportingAnEntityWhoseRootTypeTheCallerCannotReadIsRejected's own
    // scenario), is a stable, permanent rejection that retrying can never
    // change -- returned immediately, not retried, so that test's own
    // failure mode stays fast.
    private static async Task<JsonElement> ExportLineageAsync(string entityId, string clientId, string clientSecret, string scope)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, clientSecret, scope);
        JsonElement result = default;
        for (var attempt = 0; attempt < 67; attempt++)
        {
            using var request = new HttpRequestMessage(Query, "/graphql")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    query = $$"""query { exportLineage(entityId: "{{entityId}}") { bundleUrl } }""",
                }), Encoding.UTF8, "application/json"),
            };
            AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
            var response = await _hostClient.SendAsync(request);
            result = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (result.TryGetProperty("errors", out var errors))
            {
                var message = errors[0].GetProperty("message").GetString();
                if (message != "Unknown entityId.")
                    return result;
            }
            else if (result.TryGetProperty("data", out var data) && data.TryGetProperty("exportLineage", out var exportLineage) && exportLineage.ValueKind != JsonValueKind.Null)
            {
                return result;
            }
            await Task.Delay(150);
        }
        return result;
    }

    private static async Task<string> DownloadBundleAsync(string bundleUrl)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:lineage:read");
        using var request = new HttpRequestMessage(HttpMethod.Get, bundleUrl);
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [TestMethod]
    public async Task ExportingALineageProducesADownloadableBundleWithAVerifiableManifestHashAndAnAccessLogEntry()
    {
        const string appId = "lineage-export-demo-1";
        await RegisterEventTypeAsync(appId, "CaseOpened", """{ "type": "object", "properties": { "CaseId": { "type": "string" } }, "required": ["CaseId"] }""", "$.CaseId");
        await RegisterEventTypeAsync(appId, "EvidenceLinked", """{ "type": "object", "properties": { "EvidenceId": { "type": "string" }, "CaseId": { "type": "string" } }, "required": ["EvidenceId"] }""", "$.EvidenceId");

        var caseEventId = await PublishAsync(appId, "CaseOpened", """{ "CaseId": "case-1" }""");
        await PublishAsync(appId, "EvidenceLinked", """{ "EvidenceId": "evidence-1", "CaseId": "case-1" }""", [caseEventId]);
        await Task.Delay(500); // RouterWorker's own 200ms poll -- same real-Host wait every other GraphQL HTTP test already uses

        var result = await ExportLineageAsync($"{appId}:caseopened:case-1", "follower-client", "follower-client-secret", "events:lineage:read");
        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var bundleUrl = result.GetProperty("data").GetProperty("exportLineage").GetProperty("bundleUrl").GetString()!;

        var ndjson = await DownloadBundleAsync(bundleUrl);
        var bundle = LineageExportBundle.ParseNdjson(ndjson);
        Assert.AreEqual(2, bundle.Events.Count, "both the root CaseOpened event and its causally-linked EvidenceLinked descendant are exported");

        var recomputedHash = ManifestHash.Compute(bundle.Events.Select(e => e.ChainHash), bundle.Manifest.ExportedByActorId, bundle.Manifest.ExportedAt);
        Assert.AreEqual(bundle.Manifest.ManifestHash, recomputedHash);
        Assert.AreEqual(2, bundle.Manifest.EventTypeDefinitionsReferenced.Count);
        Assert.IsTrue(bundle.Manifest.EventTypeDefinitionsReferenced.Contains($"{appId}/caseopened/v1"));
        Assert.IsNull(bundle.Manifest.Rfc3161Timestamp, "ADR-086 (RFC 3161 Trusted Timestamping) is a LATER item -- never populated ahead of it");

        await using var db = OpenDb();
        var accessLogEntry = await db.AccessLogEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Action == "export" && e.ResourceRef == $"{appId}:caseopened:case-1");
        Assert.IsNotNull(accessLogEntry, "ADR-045 -- every export writes an AccessLogEntry, exactly like any other read");
    }

    [TestMethod]
    public async Task AnExportedFieldMaskedBecauseTheExportingActorLacksItsOwnClaimStaysMaskedInTheBundle()
    {
        const string appId = "lineage-export-demo-2";
        await RegisterEventTypeAsync(appId, "InvestigationOpened",
            """{ "type": "object", "properties": { "InvestigationId": { "type": "string" }, "InvestigatorNotes": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "case:notes" } } }, "required": ["InvestigationId"] }""",
            "$.InvestigationId");
        await PublishAsync(appId, "InvestigationOpened", """{ "InvestigationId": "inv-1", "InvestigatorNotes": "confidential lead" }""");
        await Task.Delay(500);

        // follower-client holds events:lineage:read but not case:notes.
        var result = await ExportLineageAsync($"{appId}:investigationopened:inv-1", "follower-client", "follower-client-secret", "events:lineage:read");
        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var bundleUrl = result.GetProperty("data").GetProperty("exportLineage").GetProperty("bundleUrl").GetString()!;

        var bundle = LineageExportBundle.ParseNdjson(await DownloadBundleAsync(bundleUrl));
        Assert.AreEqual(1, bundle.Events.Count);
        Assert.IsTrue(bundle.Events[0].Payload.Contains("masked"), "the exporting actor's own missing claim masks the field in the bundle -- the same no-bypass rule any other read already follows");
        Assert.IsFalse(bundle.Events[0].Payload.Contains("confidential lead"));
    }

    [TestMethod]
    public async Task ExportingAnUnknownEntityIdIsRejected()
    {
        var result = await ExportLineageAsync("lineage-export-demo-3:caseopened:no-such-case", "follower-client", "follower-client-secret", "events:lineage:read");
        Assert.IsTrue(result.TryGetProperty("errors", out _), "an unknown entityId has no root event at all -- rejected outright, no bundle produced");
    }

    [TestMethod]
    public async Task ExportingAnEntityWhoseRootTypeTheCallerCannotReadIsRejected()
    {
        const string appId = "lineage-export-demo-4";
        await RegisterEventTypeAsync(appId, "ClassifiedCaseOpened", """{ "type": "object", "properties": { "CaseId": { "type": "string" } }, "required": ["CaseId"] }""",
            "$.CaseId", requiredClaims: [("Read", "case:classified")]);
        await PublishAsync(appId, "ClassifiedCaseOpened", """{ "CaseId": "case-2" }""");
        await Task.Delay(500);

        // follower-client holds events:lineage:read (the GATEWAY scope) but not case:classified (the per-type Read claim).
        var result = await ExportLineageAsync($"{appId}:classifiedcaseopened:case-2", "follower-client", "follower-client-secret", "events:lineage:read");
        Assert.IsTrue(result.TryGetProperty("errors", out _), "the root event's own type carries a Read claim the caller doesn't hold -- rejected, no bundle produced, exactly as event-chains.md's own Lineage API already treats a restricted root");
    }

    [TestMethod]
    public async Task ImportingAValidBundlePreservesProvenanceAndFoldsIntoTheReceivingEnvironment()
    {
        const string sourceAppId = "lineage-export-demo-5";
        await RegisterEventTypeAsync(sourceAppId, "ReadingLogged", """{ "type": "object", "properties": { "SensorId": { "type": "string" }, "Value": { "type": "number" } }, "required": ["SensorId", "Value"] }""", "$.SensorId");
        await PublishAsync(sourceAppId, "ReadingLogged", """{ "SensorId": "sensor-import-1", "Value": 42 }""");
        await Task.Delay(500);

        var exportResult = await ExportLineageAsync($"{sourceAppId}:readinglogged:sensor-import-1", "follower-client", "follower-client-secret", "events:lineage:read");
        var bundleUrl = exportResult.GetProperty("data").GetProperty("exportLineage").GetProperty("bundleUrl").GetString()!;
        var ndjson = await DownloadBundleAsync(bundleUrl);
        var bundle = LineageExportBundle.ParseNdjson(ndjson);
        var originalEventId = bundle.Events.Single().EventId;

        // Imports preserve the original EventId (ADR-068 only says
        // SequenceNumber/ChainHash get freshened) -- proving this against a
        // GENUINELY separate receiving environment, not the same database
        // this bundle was exported from (which would legitimately collide
        // on EventId, the same idempotency identity ADR-011 already
        // establishes), the same "a fresh environment" framing this ADR's
        // own Gherkin scenario uses. A second real Host isn't needed just
        // to prove the import MECHANISM -- the tampered-bundle test above
        // already proves the real HTTP endpoint's rejection path; this one
        // calls the service directly against its own freshly-migrated db.
        var destinationDbPath = Path.Combine(Path.GetTempPath(), $"eventstore-lineage-import-dest-{Guid.NewGuid():N}.db");
        try
        {
            var destinationOptions = new DbContextOptionsBuilder<EventStoreContext>()
                .UseSqlite($"Data Source={destinationDbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
                .Options;
            await using var destinationDb = new EventStoreContext(destinationOptions, new SqliteJsonPathTranslator());
            await destinationDb.Database.MigrateAsync();

            var registry = new SchemaRegistryService(destinationDb, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(destinationDb, registry);
            var importService = new LineageExportService(destinationDb, new SqliteEventLineageQueryProvider(), registry, payloadMasker);
            var importedCount = await importService.ImportAsync(bundle, "prod-east");
            Assert.AreEqual(1, importedCount);

            var imported = await destinationDb.Events.AsNoTracking().SingleAsync(e => e.EventId == originalEventId);
            Assert.IsNotNull(imported.OriginalSequenceNumber, "the imported event carries its ORIGINAL SequenceNumber as provenance, distinct from its freshly-assigned one here");
            Assert.IsNotNull(imported.OriginalChainHash);
            Assert.AreEqual("prod-east", imported.ImportedFrom);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(destinationDbPath))
                File.Delete(destinationDbPath);
        }
    }

    [TestMethod]
    public async Task ImportingABundleWithATamperedManifestHashIsRejectedBeforeAnyWrite()
    {
        const string appId = "lineage-export-demo-6";
        await RegisterEventTypeAsync(appId, "TamperTestLogged", """{ "type": "object", "properties": { "RecordId": { "type": "string" } }, "required": ["RecordId"] }""", "$.RecordId");
        await PublishAsync(appId, "TamperTestLogged", """{ "RecordId": "rec-1" }""");
        await Task.Delay(500);

        var exportResult = await ExportLineageAsync($"{appId}:tampertestlogged:rec-1", "follower-client", "follower-client-secret", "events:lineage:read");
        var bundleUrl = exportResult.GetProperty("data").GetProperty("exportLineage").GetProperty("bundleUrl").GetString()!;
        var ndjson = await DownloadBundleAsync(bundleUrl);

        var bundle = LineageExportBundle.ParseNdjson(ndjson);
        var tamperedManifest = bundle.Manifest with { ManifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("tampered"))).ToLowerInvariant() };
        var tamperedNdjson = new LineageExportBundle(tamperedManifest, bundle.Events).ToNdjson();

        int countBefore;
        await using (var db = OpenDb())
            countBefore = await db.Events.CountAsync(e => e.EventType == "tampertestlogged");

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/lineage-imports")
        {
            Content = new StringContent(tamperedNdjson, Encoding.UTF8, "application/x-ndjson"),
        };
        AuthScenarioAssertions.AttachAuth(importRequest, _hostClient, token, key);
        var importResponse = await _hostClient.SendAsync(importRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, importResponse.StatusCode);

        await using var dbAfter = OpenDb();
        var countAfter = await dbAfter.Events.CountAsync(e => e.EventType == "tampertestlogged");
        Assert.AreEqual(countBefore, countAfter, "nothing from the tampered bundle was appended");
    }

    [TestMethod]
    public async Task PlaybackAsOfReconstructsStateInArrivalOrderAndShowsALateArrivalCorrectionLandingInPlace()
    {
        const string appId = "lineage-export-demo-7";
        await RegisterEventTypeAsync(appId, "GaugeReadingUpdated", """{ "type": "object", "properties": { "GaugeId": { "type": "string" }, "Reading": { "type": "number" } }, "required": ["GaugeId", "Reading"] }""", "$.GaugeId");

        await PublishAsync(appId, "GaugeReadingUpdated", """{ "GaugeId": "gauge-1", "Reading": 10 }""");
        await Task.Delay(200);
        await PublishAsync(appId, "GaugeReadingUpdated", """{ "GaugeId": "gauge-1", "Reading": 20 }""");
        await Task.Delay(500);

        long secondSeq;
        await using (var db0 = OpenDb())
            secondSeq = await db0.Events.Where(e => e.EntityId == $"{appId}:gaugereadingupdated:gauge-1").MaxAsync(e => e.SequenceNumber);

        // Directly append a THIRD event, chronologically stale relative to
        // the second one above -- PublishService always stamps OccurredAt
        // as real UtcNow, so a genuinely out-of-logical-order arrival can't
        // be produced through the ordinary publish path; this is the same
        // "construct the StoredEvent directly, bypass PublishService"
        // technique NonAuthoritativeCaptureScenarioAssertions' own
        // TwoServersIndependentlyDisagreeingAboutReviewStatusResolvesViaConflictFlag
        // already established for a different envelope-level flag.
        long thirdSeq;
        await using (var db = OpenDb())
        {
            var payload = """{ "GaugeId": "gauge-1", "Reading": 15 }""";
            var lateEvent = new StoredEvent
            {
                EventId = Guid.NewGuid(),
                AppId = appId,
                EntityId = $"{appId}:gaugereadingupdated:gauge-1",
                EventType = "gaugereadingupdated",
                SchemaVersion = 1,
                Payload = payload,
                PayloadHash = EventPayloadHash.Compute("gaugereadingupdated", payload, []),
                ChainHash = "",
                Status = "received",
                OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-10), // stale relative to the second event's own real-time OccurredAt
                ActorId = "test:late-arrival-injector",
            };
            await EventAppender.AppendAsync(db, lateEvent, []);
            await Task.Delay(500); // the real Host's own RouterWorker background service folds it on its next tick
            thirdSeq = lateEvent.SequenceNumber;
        }

        // Rewinding to the position immediately before the late arrival: reading is still 20 (the second event's value).
        var beforeLate = await PlaybackAsOfAsync($"{appId}:gaugereadingupdated:gauge-1", secondSeq, "follower-client", "follower-client-secret");
        Assert.IsFalse(beforeLate.TryGetProperty("errors", out _), beforeLate.ToString());
        var beforeData = beforeLate.GetProperty("data").GetProperty("playbackAsOf").GetProperty("data").GetString()!;
        Assert.IsTrue(beforeData.Contains("20"));

        // At the late arrival's own position: the correction (15) has landed IN PLACE, visibly, right here.
        var atLate = await PlaybackAsOfAsync($"{appId}:gaugereadingupdated:gauge-1", thirdSeq, "follower-client", "follower-client-secret");
        Assert.IsFalse(atLate.TryGetProperty("errors", out _), atLate.ToString());
        var atLateResult = atLate.GetProperty("data").GetProperty("playbackAsOf");
        Assert.IsTrue(atLateResult.GetProperty("data").GetString()!.Contains("15"), "arrival order, no logical-time correction -- the literal opposite of EntityStoreRow's own valid-time-corrected fold");
        Assert.IsTrue(atLateResult.GetProperty("lateArrivalCorrectionShown").GetBoolean());
    }

    private static async Task<JsonElement> PlaybackAsOfAsync(string entityId, long asOfSequenceNumber, string clientId, string clientSecret)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, clientSecret, "events:lineage:read");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""query { playbackAsOf(entityId: "{{entityId}}", asOfSequenceNumber: {{asOfSequenceNumber}}) { data extensions asOfSequenceNumber lateArrivalCorrectionShown } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
