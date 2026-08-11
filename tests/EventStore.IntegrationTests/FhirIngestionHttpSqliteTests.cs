extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

// "Bulk Ingestion & External Interchange-Format Adapters" (docs/08-build-
// plan.md, ADR-072) -- FhirAdapter's own real-HTTP surface: FHIR is
// RESTful/JSON-native and publishes with no MLLP or TCP listener involved
// at any point, unlike HL7v2's own dedicated Hl7V2MllpListener (covered
// separately, real TCP/MLLP).
[TestClass]
public class FhirIngestionHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-fhir-http-{Guid.NewGuid():N}.db");
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

    [TestMethod]
    public async Task AFhirPatientResourcePostedOverOrdinaryHttpsPublishesThroughTheOrdinaryPathWithNoMllpOrTcpListenerInvolved()
    {
        const string appId = "fhir-http-demo";
        var (operatorToken, operatorKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/PatientAdmitted")
        {
            Content = JsonContent.Create(new
            {
                appId, jsonSchema = """{ "type": "object", "properties": { "PatientId": { "type": "string" }, "LastName": { "type": "string" }, "FirstName": { "type": "string" } }, "required": ["PatientId"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.PatientId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _hostClient, operatorToken, operatorKey);
        Assert.AreEqual(HttpStatusCode.Created, (await _hostClient.SendAsync(registerRequest)).StatusCode);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var fhirResource = """{ "resourceType": "Patient", "id": "fhir-http-pat-1", "name": [{ "family": "Nguyen", "given": ["Anh"] }] }""";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/interchange/Fhir/{appId}") { Content = new StringContent(fhirResource, Encoding.UTF8, "application/fhir+json") };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        await using var db = OpenDb();
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == "patientadmitted");
        Assert.IsTrue(stored.Payload.Contains("fhir-http-pat-1"));
        Assert.AreNotEqual("accepted", stored.AuthorityStatus, "non-authoritative capture is the default for EMR-sourced data (ADR-035/072)");
    }

    [TestMethod]
    public async Task AMalformedFhirResourceIsRejected400BeforeAnyPublishAttempt()
    {
        const string appId = "fhir-http-demo-2";
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/interchange/Fhir/{appId}") { Content = new StringContent("""{ "resourceType": "Observation" }""", Encoding.UTF8, "application/fhir+json") };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

        using var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, "FhirAdapter only handles Patient in this build stage -- an unsupported resourceType is rejected, not silently ignored");
    }

    private static EventStoreContext OpenDb() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());
}
