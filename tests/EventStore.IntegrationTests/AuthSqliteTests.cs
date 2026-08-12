extern alias DevIdpAssembly;

using System.Net;
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

[TestClass]
public class AuthSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-auth-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        // Pre-fetch DevIdp's real discovery document + JWKS through the
        // in-memory TestServer client (HttpDocumentRetriever(_devIdpClient),
        // not a real network HttpClient), then hand the Host's JwtBearer
        // handler that configuration via a StaticConfigurationManager set
        // directly on Options.ConfigurationManager -- NOT Options.Configuration.
        // The framework's own internal JwtBearerOptions post-configure step
        // (registered by Program.cs's AddJwtBearer call, which runs BEFORE
        // this test's PostConfigure) already converts whatever
        // Options.Configuration held AT THAT TIME into a real
        // ConfigurationManager fetching from the stale appsettings.
        // Development.json Authority -- setting Options.Configuration here
        // arrives too late to affect that already-built ConfigurationManager,
        // which is the field the handler actually reads. Replacing
        // ConfigurationManager itself sidesteps that ordering entirely.
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
    public async Task AllAuthScenarios()
    {
        await AuthScenarioAssertions.RequestWithoutAuthorizationHeaderIsRejected(_hostClient);
        await AuthScenarioAssertions.RequestWithAnInvalidTokenIsRejected(_hostClient);
        await AuthScenarioAssertions.TokenMissingTheRequiredScopeIsRejectedWith403(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.RegistryPutWithoutRegistryAdminScopeIsRejectedWith403(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.RegisteringAnEventTypeAndPublishingToItWithTheRightScopesSucceeds(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.OpenApiAndAsyncApiStayAnonymouslyReadable(_hostClient);
        await AuthScenarioAssertions.AnAllowedOriginGetsCorsHeadersAndADisallowedOriginDoesNot(
            _hostClient, allowedOrigin: "http://localhost:5173", disallowedOrigin: "http://evil.example");

        await AuthScenarioAssertions.ARequestWithAValidBearerTokenButNoDpopProofIsRejectedWith401(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.ARequestWithADpopProofSignedByADifferentKeyIsRejectedWith401(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.ReplayingAnAlreadyUsedDpopProofIsRejectedWith401(_hostClient, _devIdpClient);
        await AuthScenarioAssertions.ATenantScopedTokenCanAdministerItsOwnAppIdButNotAnother(_hostClient, _devIdpClient);
    }

    // ADR-002 -- "a single config flag (SpecEndpoints:Enabled) turns the
    // routes off completely... the actual MapGet registrations are
    // conditional on it," found unimplemented by a design-compliance
    // audit and built directly (SpecGenerationEndpoints.cs). A short-
    // lived second factory against the same already-migrated database --
    // this scenario only reads, no state to isolate from the class's own
    // shared _hostClient.
    [TestMethod]
    public async Task SpecEndpointsCanBeDisabledViaConfig()
    {
        using var disabledFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
            builder.UseSetting("SpecEndpoints:Enabled", "false");
        });
        using var disabledClient = disabledFactory.CreateClient();

        Assert.AreEqual(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/openapi.json")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/asyncapi.json")).StatusCode);
        // ADR-025 -- both UI routes are gated by the exact same
        // SpecEndpoints:Enabled early-return as the raw JSON documents
        // they render; disabling one disables all four together.
        Assert.AreEqual(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/scalar")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/asyncapi-ui")).StatusCode);
    }

    // ADR-025 -- both UI routes are pure presentation over the existing
    // JSON documents; real HTTP is the only way to prove Scalar's own
    // OpenApiRoutePattern wiring and the static AsyncAPI page actually
    // come back, not just that the code compiles.
    [TestMethod]
    public async Task ScalarUiServesAnHtmlPageAtScalar()
    {
        using var response = await _hostClient.GetAsync("/scalar");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task AsyncApiUiServesTheStandaloneRendererPointedAtAsyncApiJson()
    {
        using var response = await _hostClient.GetAsync("/asyncapi-ui");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "AsyncApiStandalone.render");
        StringAssert.Contains(html, "url: '/asyncapi.json'");
    }
}
