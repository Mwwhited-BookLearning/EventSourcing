extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EventStore.Abstractions;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.LineageExport;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Timestamping;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "RFC 3161 Trusted Timestamping" (docs/08-build-plan.md, ADR-086) -- two
// consumers, Digital Sign-Off's Signature and Lineage Export's manifest,
// both proven against a REAL fake TSA (TimestampingTestSupport.cs, real
// RSA/CMS crypto, no shortcuts) rather than a hand-rolled stub returning
// arbitrary bytes -- the whole point is that the resulting token
// independently verifies via the BCL's own Rfc3161TimestampToken, which a
// fake byte blob could never do. Sqlite-only, matching DigitalSignOffHttp
// SqliteTests' own reasoning: this item's logic (opt-in gating, hash
// selection, HTTP request/response shaping) is entirely provider-agnostic.
[TestClass]
public class TimestampingHttpSqliteTests
{
    private static string _dbPath = default!;
    private static X509Certificate2 _tsaCertificate = default!;
    private static IHost _fakeTsaServer = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-timestamping-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        (_tsaCertificate, _fakeTsaServer) = await TimestampingTestSupport.StartFakeTsaAsync();

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
            builder.UseSetting("Authentication:Authority", _devIdpClient.BaseAddress!.ToString());
            // A syntactically-real absolute URL -- never actually resolved
            // over a real socket, since the typed HttpClient's primary
            // handler is overridden below to route in-process into the
            // fake TSA's own TestServer instead.
            builder.UseSetting("Timestamping:TsaUrl", "http://fake-tsa.test/tsa");
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                    o.RequireHttpsMetadata = false;
                });
                services.AddHttpClient<ITimestampAuthorityClient, HttpTimestampAuthorityClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _fakeTsaServer.GetTestServer().CreateHandler());
            });
        });
        _hostClient = _hostFactory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _hostClient.Dispose();
        _hostFactory.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        await _fakeTsaServer.StopAsync();
        _fakeTsaServer.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

    private static async Task RegisterSignedTypeAsync(string appId, string typeName, string acrValue, bool enableRfc3161Timestamp)
    {
        var (operatorToken, operatorKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{typeName}")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.Id",
                parentValidationMode = "Permissive",
                requiredSignature = new { acrValues = new[] { acrValue }, maxAge = (int?)null, enableRfc3161Timestamp },
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, operatorToken, operatorKey);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task APublishOptingIntoRfc3161TimestampObtainsAnIndependentlyVerifiableTokenOverHashOfChainHash()
    {
        const string appId = "timestamping-http-demo-1";
        const string typeName = "RequiresTimestampedSignOff1";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterSignedTypeAsync(appId, typeName, acrValue, enableRfc3161Timestamp: true);

        var (steppedUpToken, key) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "publisher-client", "publisher-client-secret", "events:publish", acr: acrValue);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "Id": "rec-1" }""", meaning = "approved" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, steppedUpToken, key);

        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());

        await using var db = OpenDb();
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == typeName.ToLowerInvariant());
        Assert.IsNotNull(stored.Signature);
        Assert.IsNotNull(stored.Signature.RFC3161Timestamp, "RequiredSignature.EnableRfc3161Timestamp was true -- a token must have been obtained and stored");

        Assert.IsTrue(Rfc3161TimestampToken.TryDecode(stored.Signature.RFC3161Timestamp, out var token, out _), "the stored bytes must be a real, decodable RFC 3161 TimeStampToken");
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(stored.ChainHash));
        var verified = token!.VerifySignatureForHash(expectedHash, HashAlgorithmName.SHA256, out var signerCert, new X509Certificate2Collection(_tsaCertificate));
        Assert.IsTrue(verified, "the token must independently verify against the fake TSA's own certificate, over SHA-256(ChainHash) -- not merely be present");
        Assert.AreEqual(_tsaCertificate.Subject, signerCert!.Subject);
    }

    [TestMethod]
    public async Task APublishNotOptingIntoRfc3161TimestampLeavesItNull()
    {
        const string appId = "timestamping-http-demo-2";
        const string typeName = "RequiresUntimestampedSignOff2";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterSignedTypeAsync(appId, typeName, acrValue, enableRfc3161Timestamp: false);

        var (steppedUpToken, key) = await AuthScenarioAssertions.GetTokenAsync(
            _devIdpClient, "publisher-client", "publisher-client-secret", "events:publish", acr: acrValue);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "Id": "rec-1" }""", meaning = "approved" }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, steppedUpToken, key);

        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());

        await using var db = OpenDb();
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == typeName.ToLowerInvariant());
        Assert.IsNotNull(stored.Signature);
        Assert.IsNull(stored.Signature.RFC3161Timestamp, "no opt-in -- no TSA call should ever have been made");
    }

    [TestMethod]
    public async Task ALineageExportsManifestGetsAnRfc3161TimestampOverItsOwnManifestHashWhenATsaIsConfigured()
    {
        const string appId = "timestamping-export-demo-1";
        await using var db = OpenDb();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        await registry.RegisterAsync("RecordCreated", new RegisterEventTypeRequest(
            appId, """{ "type": "object", "properties": { "RecordId": { "type": "string" } }, "required": ["RecordId"] }""",
            [], "Full", "$.RecordId", "Permissive", null, null, null));

        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var lineageQueryProvider = new SqliteEventLineageQueryProvider();

        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(), AppId = appId, EntityId = $"{appId}:recordcreated:rec-1", EventType = "recordcreated", SchemaVersion = 1,
            Payload = """{ "RecordId": "rec-1" }""", PayloadHash = "irrelevant-for-this-test",
            ChainHash = "", Status = "received", OccurredAt = DateTimeOffset.UtcNow, ActorId = "system:test",
        };
        await EventAppender.AppendAsync(db, storedEvent, []);

        var fakeTsaHandler = _fakeTsaServer.GetTestServer().CreateHandler();
        var timestampAuthorityClient = new HttpTimestampAuthorityClient(
            new HttpClient(fakeTsaHandler), Options.Create(new TimestampingOptions { TsaUrl = "http://fake-tsa.test/tsa" }));
        var exportService = new LineageExportService(db, lineageQueryProvider, registry, payloadMasker, timestampAuthorityClient);

        var user = TestClaimsPrincipal.None;
        var bundle = await exportService.ExportAsync(storedEvent.EntityId, user, "auditor-1");

        Assert.IsNotNull(bundle.Manifest.Rfc3161Timestamp);
        var tokenBytes = Convert.FromBase64String(bundle.Manifest.Rfc3161Timestamp);
        Assert.IsTrue(Rfc3161TimestampToken.TryDecode(tokenBytes, out var token, out _));
        var expectedHash = Convert.FromHexString(bundle.Manifest.ManifestHash);
        var verified = token!.VerifySignatureForHash(expectedHash, HashAlgorithmName.SHA256, out _, new X509Certificate2Collection(_tsaCertificate));
        Assert.IsTrue(verified, "the manifest's token must independently verify over the manifest hash's own raw bytes -- ManifestHash IS already a SHA-256 digest, submitted directly, not re-hashed");
    }

    [TestMethod]
    public async Task ALineageExportsManifestLeavesRfc3161TimestampNullWhenNoTsaIsConfigured()
    {
        const string appId = "timestamping-export-demo-2";
        await using var db = OpenDb();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        await registry.RegisterAsync("RecordCreated", new RegisterEventTypeRequest(
            appId, """{ "type": "object", "properties": { "RecordId": { "type": "string" } }, "required": ["RecordId"] }""",
            [], "Full", "$.RecordId", "Permissive", null, null, null));
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);

        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(), AppId = appId, EntityId = $"{appId}:recordcreated:rec-1", EventType = "recordcreated", SchemaVersion = 1,
            Payload = """{ "RecordId": "rec-1" }""", PayloadHash = "irrelevant-for-this-test",
            ChainHash = "", Status = "received", OccurredAt = DateTimeOffset.UtcNow, ActorId = "system:test",
        };
        await EventAppender.AppendAsync(db, storedEvent, []);

        // timestampAuthorityClient omitted entirely -- the no-TSA-configured case.
        var exportService = new LineageExportService(db, new SqliteEventLineageQueryProvider(), registry, payloadMasker);
        var bundle = await exportService.ExportAsync(storedEvent.EntityId, TestClaimsPrincipal.None, "auditor-1");

        Assert.IsNull(bundle.Manifest.Rfc3161Timestamp);
    }
}
