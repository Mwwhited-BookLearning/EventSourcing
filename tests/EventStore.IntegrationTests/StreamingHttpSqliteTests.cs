extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

// "Streaming Channels" (docs/08-build-plan.md) -- the one scenario that
// genuinely needs the real ASP.NET Core pipeline, not a direct service
// call: HTTP Range-request handling (206 Partial Content, Content-Range)
// is Results.Bytes(enableRangeProcessing: true)'s own response-writing
// behavior, only observable end to end. Same two-WebApplicationFactory-
// TestServer pattern AuthSqliteTests already established.
[TestClass]
public class StreamingHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-streaming-http-{Guid.NewGuid():N}.db");
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
    public async Task ARangeRequestAgainstAMediaChannelReturns206PartialContentWithTheExactRequestedByteRange()
    {
        const string channelId = "streaming-http-demo-1";
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "telemetry-client", "telemetry-client-secret", "telemetry:ingest telemetry:read");

        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, $"/telemetry/channels/{channelId}")
        {
            Content = JsonContent.Create(new
            {
                appId = "streaming-http-app", entityId = "cam:1", contentKind = "Media",
                mimeType = "video/h264", origin = "Origin",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _hostClient, token, key);
        var registerResponse = await _hostClient.SendAsync(registerRequest);
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode);

        var chunkBytes = Enumerable.Range(0, 3000).Select(i => (byte)(i % 256)).ToArray();
        using var ingestRequest = new HttpRequestMessage(HttpMethod.Post, $"/telemetry/{channelId}/samples")
        {
            Content = JsonContent.Create(new
            {
                samples = new[] { new { timestamp = "2026-07-29T10:00:00Z", value = Convert.ToBase64String(chunkBytes) } },
            }),
        };
        AuthScenarioAssertions.AttachAuth(ingestRequest, _hostClient, token, key);
        var ingestResponse = await _hostClient.SendAsync(ingestRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, ingestResponse.StatusCode);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/telemetry/{channelId}/samples");
        rangeRequest.Headers.Range = new RangeHeaderValue(1000, 1999);
        AuthScenarioAssertions.AttachAuth(rangeRequest, _hostClient, token, key);
        var rangeResponse = await _hostClient.SendAsync(rangeRequest);

        Assert.AreEqual(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.IsNotNull(rangeResponse.Content.Headers.ContentRange);

        var body = await rangeResponse.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(chunkBytes[1000..2000], body);
    }
}
