using System.Net.Http.Headers;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "i18n/l10n Architectural Scope" (docs/08-build-plan.md, ADR-087) --
// RFC 9110 §12 Accept-Language negotiation via ASP.NET Core's own
// RequestLocalizationMiddleware (HostCoreExtensions.cs), proven over a
// real HTTP round trip against the actual anonymous /openapi.json
// endpoint -- no DevIdp/token setup needed, since negotiation itself
// happens in middleware ahead of authentication and this route never
// requires a claim.
[TestClass]
public class LocalizationHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-localization-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _hostFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}"));
        _hostClient = _hostFactory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _hostClient.Dispose();
        _hostFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [TestMethod]
    public async Task ASupportedAcceptLanguageIsNegotiatedAndEchoedBackAsContentLanguage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi.json");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("ar-SA"));

        var response = await _hostClient.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("ar-SA", response.Content.Headers.ContentLanguage.Single());
    }

    [TestMethod]
    public async Task AnAcceptLanguageNamingAnUnsupportedCultureFallsBackToTheDefaultRatherThanErroring()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi.json");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("de-DE"));

        var response = await _hostClient.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("en-US", response.Content.Headers.ContentLanguage.Single());
    }

    [TestMethod]
    public async Task AWeightedAcceptLanguageListNegotiatesTheHighestPrioritySupportedCulture()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi.json");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("de-DE", 0.9));
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("fr-FR", 0.8));

        var response = await _hostClient.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("fr-FR", response.Content.Headers.ContentLanguage.Single());
    }

    [TestMethod]
    public async Task NoAcceptLanguageHeaderAtAllStillGetsTheDefaultCultureEchoedBack()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi.json");

        var response = await _hostClient.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("en-US", response.Content.Headers.ContentLanguage.Single());
    }
}
