extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

// "Binary Attachments" (docs/08-build-plan.md) -- the one scenario that
// genuinely needs the real ASP.NET Core pipeline: HTTP Range-request
// handling (206 Partial Content, Content-Range) is Results.Bytes
// (enableRangeProcessing: true)'s own response-writing behavior, the same
// mechanism already proven for Streaming Channels' Media playback, reused
// unchanged here per ADR-032's own text.
[TestClass]
public class AttachmentHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-attachments-http-{Guid.NewGuid():N}.db");
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
    public async Task ARangeRequestAgainstAContentAddressedAttachmentReturns206PartialContentWithTheExactRequestedByteRange()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "attachments-client", "attachments-client-secret", "attachments:ingest attachments:read");

        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 256)).ToArray();
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/attachments")
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } },
        };
        AuthScenarioAssertions.AttachAuth(uploadRequest, _hostClient, token, key);
        var uploadResponse = await _hostClient.SendAsync(uploadRequest);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);
        var contentHash = (await uploadResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contentHash").GetString();

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/attachments/{contentHash}");
        rangeRequest.Headers.Range = new RangeHeaderValue(1000, 1999);
        AuthScenarioAssertions.AttachAuth(rangeRequest, _hostClient, token, key);
        var rangeResponse = await _hostClient.SendAsync(rangeRequest);

        Assert.AreEqual(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.IsNotNull(rangeResponse.Content.Headers.ContentRange);

        var body = await rangeResponse.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(bytes[1000..2000], body);
    }
}
