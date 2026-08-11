extern alias DevIdpAssembly;

using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Projections.Host;
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
using Samples.Orders.Projections;

namespace EventStore.IntegrationTests;

// Unlike every other item's tests, this drives real HTTP end to end -- the
// same two-WebApplicationFactory-TestServer pattern AuthSqliteTests already
// established, plus a real ProjectionHost<OrderSummary> wired to those same
// TestServer HttpClients via FixedHttpClientFactory. Single-provider only
// (Sqlite): docs/09-cqrs-read-models.md's own "no per-provider build split
// here... one EF Core provider is sufficient" note, unlike every write-side
// item's own 3-provider matrix.
[TestClass]
public class ProjectionsSqliteTests
{
    private static string _dbPath = default!;
    private static string _projectionsDbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-projections-write-{Guid.NewGuid():N}.db");
        _projectionsDbPath = Path.Combine(Path.GetTempPath(), $"orders-projections-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        // Same real cross-TestServer JwtBearer wiring as AuthSqliteTests -- see
        // that class's own comment for why ConfigurationManager, not
        // Configuration, is the field that must be set.
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

        using var projectionsDb = CreateProjectionsDb();
        await projectionsDb.Database.MigrateAsync();
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
        if (File.Exists(_projectionsDbPath))
            File.Delete(_projectionsDbPath);
    }

    private static OrdersProjectionsDbContext CreateProjectionsDb()
    {
        var options = new DbContextOptionsBuilder<OrdersProjectionsDbContext>()
            .UseSqlite($"Data Source={_projectionsDbPath}")
            .Options;
        return new OrdersProjectionsDbContext(options);
    }

    [TestMethod]
    public async Task AllProjectionScenarios()
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
        var projection = new OrderSummaryProjection();
        var hostOptions = Options.Create(new ProjectionHostOptions { AppId = "orders-demo" });

        var services = new ServiceCollection();
        services.AddDbContext<OrdersProjectionsDbContext>(o => o.UseSqlite($"Data Source={_projectionsDbPath}"));
        services.AddScoped<ProjectionsDbContext>(sp => sp.GetRequiredService<OrdersProjectionsDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var host = new ProjectionHost<OrderSummary>(scopeFactory, projection, followClient, hostOptions, NullLogger<ProjectionHost<OrderSummary>>.Instance);

        await ProjectionsScenarioAssertions.AFullEventEstablishesTheReadModelRowFromScratch(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
        await ProjectionsScenarioAssertions.APartialEventMergesOntoExistingStateLeavingUntouchedFieldsAlone(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
        await ProjectionsScenarioAssertions.IndependentPartialEventsEachMergeWithoutClobberingTheOthersFields(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
        await ProjectionsScenarioAssertions.AMaskedOrAbsentFieldInAPartialPayloadIsIgnoredOnMergeNeverOverlaidAsAPlaceholder(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
        await ProjectionsScenarioAssertions.RegisteringAnEventTypeWithoutChangeKindIsRejected(_hostClient, _devIdpClient);
        await ProjectionsScenarioAssertions.FullRebuildFromScratchReproducesTheSameEndStateAsIncrementalApplication(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
        await ProjectionsScenarioAssertions.IncrementalResumeAfterDowntimeDeliversNoGapAndNoDuplicate(_hostClient, _devIdpClient, host, projection, CreateProjectionsDb);
    }
}
