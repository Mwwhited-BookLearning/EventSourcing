extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventStore.Dpop;
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

namespace EventStore.IntegrationTests;

// ADR-045 -- AccessLog's own dedicated test file (DelegatedGrantsRbacFederation
// HttpSqliteTests' own header comment flags this split): every read through
// any surface writes an AccessLogEntry recording the reader and trust basis,
// and tampering with a past entry is detectable by replaying its independent
// hash chain.
[TestClass]
public class AccessLogHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-access-log-http-{Guid.NewGuid():N}.db");
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

    private static EventStoreContext OpenDirectDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

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

    [TestMethod]
    public async Task AnOrdinaryLineageQueryWritesAnAccessLogEntryRecordingTheReaderAsAuthoritative()
    {
        const string appId = "access-log-demo-1";
        await RegisterPatientEnrolledAsync(appId);
        var eventId = await PublishPatientAsync(appId, "p-1");
        await Task.Delay(500); // RouterWorker's own 200ms poll

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:lineage:read");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""query { event(eventId: "{{eventId}}") { eventId } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.TryGetProperty("errors", out _), body.ToString());

        await using var db = OpenDirectDb();
        var entry = await db.AccessLogEntries
            .Where(e => e.ResourceRef == eventId.ToString() && e.Action == "query")
            .OrderByDescending(e => e.SequenceNumber)
            .FirstOrDefaultAsync();
        Assert.IsNotNull(entry, "expected an AccessLogEntry for this lineage query");
        Assert.AreEqual("follower-client", entry!.ReaderActorId);
        Assert.AreEqual("Authoritative", entry.ReaderTrustBasis);
        Assert.AreEqual("Authoritative", entry.ViewAccessed);
    }

    [TestMethod]
    public async Task ARevealFieldCallWritesAnAccessLogEntryWithActionReveal()
    {
        const string appId = "access-log-demo-2";
        await RegisterPatientEnrolledAsync(appId);
        var eventId = await PublishPatientAsync(appId, "p-2");
        await Task.Delay(500);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""mutation { revealField(entityId: "{{appId}}:patientenrolled:p-2", eventId: "{{eventId}}", fieldPath: "$.Ssn") { value } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.TryGetProperty("errors", out _), body.ToString());

        await using var db = OpenDirectDb();
        var entry = await db.AccessLogEntries
            .Where(e => e.Action == "reveal" && e.ResourceRef == $"{appId}:patientenrolled:p-2:$.Ssn")
            .OrderByDescending(e => e.SequenceNumber)
            .FirstOrDefaultAsync();
        Assert.IsNotNull(entry, "expected an AccessLogEntry for this revealField call");
        Assert.AreEqual("clinician-spa-client", entry!.ReaderActorId);
    }

    [TestMethod]
    public async Task TamperingWithAPastAccessLogEntryIsDetectableByReplayingItsIndependentHashChain()
    {
        const string appId = "access-log-demo-3";
        await RegisterPatientEnrolledAsync(appId);
        var eventId = await PublishPatientAsync(appId, "p-3");
        await Task.Delay(500);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:lineage:read");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""query { event(eventId: "{{eventId}}") { eventId } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        (await _hostClient.SendAsync(request)).EnsureSuccessStatusCode();

        long tamperedSequence;
        await using (var db = OpenDirectDb())
        {
            var entry = await db.AccessLogEntries
                .Where(e => e.ResourceRef == eventId.ToString())
                .OrderByDescending(e => e.SequenceNumber)
                .FirstAsync();
            tamperedSequence = entry.SequenceNumber;
            // A direct-database edit to a field the ChainHash covers, left
            // with the ChainHash column itself untouched -- the exact
            // tamper this verifier exists to catch (ADR-019's own promise,
            // reused for this independent chain).
            entry.ResourceRef = "tampered-resource-ref";
            await db.SaveChangesAsync();
        }

        var (adminToken, adminKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"/access-log/verify?throughSequenceNumber={tamperedSequence}");
        AuthScenarioAssertions.AttachAuth(verifyRequest, _hostClient, adminToken, adminKey);
        var verifyResponse = await _hostClient.SendAsync(verifyRequest);
        Assert.AreEqual(HttpStatusCode.OK, verifyResponse.StatusCode, await verifyResponse.Content.ReadAsStringAsync());
        var verifyBody = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(verifyBody.GetProperty("verified").GetBoolean());
        Assert.AreEqual(tamperedSequence, verifyBody.GetProperty("firstDivergentSequenceNumber").GetInt64());
    }
}
