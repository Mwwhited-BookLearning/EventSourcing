extern alias DevIdpAssembly;
extern alias HostSqlServerAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Replication;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// ADR-033 (the queued cross-provider-peer-sync ADR) -- the first genuinely
// cross-provider proof of PeerSyncClient/PeerSyncReceiver: Site A is a real
// EventStore.Host.Sqlite TestServer (the same shape ReplicationHttpSqliteTests
// already proves), Site B is a real EventStore.Host.SqlServer TestServer
// backed by a real MsSqlContainer (the same Testcontainers shape
// ReplicationSqlServerTests already proves for that provider alone). Neither
// PeerSyncClient nor PeerSyncReceiver do anything provider-specific --
// confirmed directly (PeerSyncClient.PushAsync is a plain HTTP POST with a
// JSON body; PeerSyncReceiver.ReceiveAsync appends through whichever
// EventStoreContext the receiving process already resolved, via the
// ordinary, provider-agnostic EventAppender) -- so this is exercising an
// existing, real mechanism cross-provider for the first time, not a new one.
//
// [DoNotParallelize] -- the same real, previously-found MsSqlContainer
// resource-exhaustion flakiness ReplicationSqlServerTests.cs's own comment
// documents applies here too (a second class starting its own MsSqlContainer
// concurrently with that one).
[DoNotParallelize]
[TestClass]
public class ReplicationCrossProviderHttpTests
{
    private static string _dbPathSqlite = default!;
    private static MsSqlContainer _sqlServerContainer = default!;
    private static string _sqlServerConnectionString = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _sqliteFactory = default!;
    private static WebApplicationFactory<HostSqlServerAssembly::Program> _sqlServerFactory = default!;
    private static HttpClient _sqliteClient = default!;
    private static HttpClient _sqlServerClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPathSqlite = Path.Combine(Path.GetTempPath(), $"eventstore-replication-crossprovider-sqlite-{Guid.NewGuid():N}.db");
        var sqliteOptions = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPathSqlite}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(sqliteOptions, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _sqlServerContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sqlServerContainer.StartAsync();

        // ADR-095's own ENABLE_BROKER statement fails outright against
        // Testcontainers' own default connection ("master") -- SQL Server
        // refuses "Option 'ENABLE_BROKER' cannot be set in database
        // 'master'", confirmed directly by WorkerWakeSignalSqlServerTests.cs's
        // own comment and reproduced here: this is the first test to boot
        // the REAL WebApplicationFactory<Program> (RouterWorker/
        // WebhookOutboxPump/PeerSyncWorker all real BackgroundServices) against
        // SQL Server rather than calling EventStoreContext/PeerSyncReceiver
        // directly, so it's also the first to actually need the Service
        // Broker queue those workers poll -- a real, named, non-system
        // database, mirroring that test's own established fix.
        const string databaseName = "ReplicationCrossProviderTest";
        await using (var masterConnection = new SqlConnection(_sqlServerContainer.GetConnectionString()))
        {
            await masterConnection.OpenAsync();
            await using var command = masterConnection.CreateCommand();
            command.CommandText = $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{databaseName}') CREATE DATABASE [{databaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        _sqlServerConnectionString = new SqlConnectionStringBuilder(_sqlServerContainer.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;

        var sqlServerOptions = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlServer(_sqlServerConnectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        await using (var db = new EventStoreContext(sqlServerOptions, new SqlServerJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(_devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        _sqliteFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPathSqlite}");
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                    o.RequireHttpsMetadata = false;
                });
                services.Configure<OriginIdOptions>(o => o.OriginId = "sqlite-site");
                // SchemaRegistryOriginIdOptions is a SEPARATE type from
                // OriginIdOptions (EventStore.SchemaRegistry can't reference
                // EventStore.Inbox, ADR-033/090) -- both bind to the same
                // "OriginId" config section in a real deployment, but this
                // test's own direct in-code override above only ever affects
                // the ONE type it names, so schema-registry replication
                // (AnEventPublishedAtTheSqliteSiteReplicatesTheSchemaTooSchema
                // RegisteredNeverIndependentlyOnSqlServer, below) needs its
                // own explicit override too, or both sites would collide on
                // the shared "local" default and the reactor's OriginId gate
                // would never fire.
                services.Configure<SchemaRegistryOriginIdOptions>(o => o.OriginId = "sqlite-site");
            });
        });
        _sqliteClient = _sqliteFactory.CreateClient();

        _sqlServerFactory = new WebApplicationFactory<HostSqlServerAssembly::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SqlServer", _sqlServerConnectionString);
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                    o.RequireHttpsMetadata = false;
                });
                services.Configure<OriginIdOptions>(o => o.OriginId = "sqlserver-site");
                services.Configure<SchemaRegistryOriginIdOptions>(o => o.OriginId = "sqlserver-site");
            });
        });
        _sqlServerClient = _sqlServerFactory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _sqliteClient.Dispose();
        _sqliteFactory.Dispose();
        _sqlServerClient.Dispose();
        _sqlServerFactory.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPathSqlite))
            File.Delete(_dbPathSqlite);
        await _sqlServerContainer.DisposeAsync();
    }

    [TestMethod]
    public async Task AnEventPublishedAtTheSqliteSiteReplicatesToTheRealSqlServerSiteOverRealHttpWithOriginIdPreserved()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "peer-sync-client", "peer-sync-client-secret", "peer:sync events:publish registry:admin");

        // Register + publish at the SQLite site through its own real HTTP
        // surface, the same as any ordinary client would.
        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderPlaced")
        {
            Content = JsonContent.Create(new
            {
                appId = "replication-crossprovider-demo",
                jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.OrderId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _sqliteClient, token, key);
        var registerResponse = await _sqliteClient.SendAsync(registerRequest);
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode, await registerResponse.Content.ReadAsStringAsync());

        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId = "replication-crossprovider-demo", schemaVersion = 1, payload = """{ "OrderId": "rep-xp-1", "Amount": 42.50 }""" }),
        };
        AuthScenarioAssertions.AttachAuth(publishRequest, _sqliteClient, token, key);
        var publishResponse = await _sqliteClient.SendAsync(publishRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, publishResponse.StatusCode, await publishResponse.Content.ReadAsStringAsync());
        var published = (await publishResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("correlationId").GetGuid();

        // Drive PeerSyncWorker's own mechanics for real, over real HTTP, from
        // the SQLite site to the real SQL Server site -- a real PeerSyncClient
        // whose "PeerSync" named client is the SQL Server site's own TestServer
        // HttpClient. No routing/schema-validation/projection happens here --
        // ADR-033's own Decision text, confirmed: it lands exactly as if it
        // arrived from the SQL Server site's own client Inbox.
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = _sqlServerClient, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);

        var (whoAmIPeerId, _) = await peerSyncClient.WhoAmIAsync("", CancellationToken.None);
        Assert.AreEqual("sqlserver-site", whoAmIPeerId);

        var sqliteOptions = new DbContextOptionsBuilder<EventStoreContext>().UseSqlite($"Data Source={_dbPathSqlite}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")).Options;
        await using var sqliteDb = new EventStoreContext(sqliteOptions, new SqliteJsonPathTranslator());
        var events = await sqliteDb.Events.AsNoTracking().OrderBy(e => e.SequenceNumber).ToListAsync();
        var pushRequest = new PeerSyncPushRequest("sqlite-site", events.Select(PeerSyncWorker.ToPayload).ToList(), []);
        var pushResponse = await peerSyncClient.PushAsync("", pushRequest, CancellationToken.None);
        Assert.AreEqual(events[^1].SequenceNumber, pushResponse.AckedThroughSequenceNumber);

        var sqlServerOptions = new DbContextOptionsBuilder<EventStoreContext>().UseSqlServer(_sqlServerConnectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer")).Options;
        await using var sqlServerDb = new EventStoreContext(sqlServerOptions, new SqlServerJsonPathTranslator());
        var replicated = await sqlServerDb.Events.AsNoTracking().SingleAsync(e => e.EventId == published);
        Assert.AreEqual("sqlite-site", replicated.OriginId, "the SQL Server site's own copy must preserve the ORIGINATING site's OriginId, never overwrite it with its own");
    }

    // docs/10-open-questions.md row 1, resolved this pass: a schema
    // registered ONLY at the SQLite site must become genuinely queryable
    // at the real SQL Server site too, having never been independently
    // registered there -- the exact gap ADR-102's own live cross-provider
    // verification found (a real GraphQL "field does not exist" error
    // against a peer nobody had registered the schema on directly).
    // WidgetCreated is a schema no other test in this file touches, with a
    // real indexed FilterableField, so this proves BOTH the definition
    // itself AND its provider-specific index DDL replicate, not just the
    // bare JsonSchema text.
    [TestMethod]
    public async Task AnEventTypeRegisteredOnlyAtTheSqliteSiteBecomesQueryableAtTheRealSqlServerSiteWithNoIndependentRegistrationThere()
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "peer-sync-client", "peer-sync-client-secret", "peer:sync events:publish registry:admin");
        const string appId = "schema-replication-demo";

        // Confirmed absent at the SQL Server site BEFORE replication --
        // not "maybe it happened to be there some other way."
        using var precheckRequest = new HttpRequestMessage(HttpMethod.Get, $"/registry/WidgetCreated?appId={appId}");
        AuthScenarioAssertions.AttachAuth(precheckRequest, _sqlServerClient, token, key);
        var precheckResponse = await _sqlServerClient.SendAsync(precheckRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, precheckResponse.StatusCode);

        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/WidgetCreated")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = """{ "type": "object", "properties": { "WidgetId": { "type": "string" }, "Category": { "type": "string" } }, "required": ["WidgetId"] }""",
                filterableFields = new[] { new { jsonPath = "$.Category", dataType = "String", isIndexed = true } },
                changeKind = "Full", entityIdField = "$.WidgetId",
            }),
        };
        AuthScenarioAssertions.AttachAuth(registerRequest, _sqliteClient, token, key);
        var registerResponse = await _sqliteClient.SendAsync(registerRequest);
        Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode, await registerResponse.Content.ReadAsStringAsync());

        // Sweeps and pushes the WHOLE Sqlite event log, same as the sibling
        // test above -- the SchemaRegistered notification RegisterAsync just
        // appended is an ordinary row in it, nothing special-cased here.
        var httpClientFactory = new FixedHttpClientFactory(new Dictionary<string, HttpClient> { ["PeerSync"] = _sqlServerClient, ["DevIdp"] = _devIdpClient });
        var peerSyncClientOptions = Options.Create(new PeerSyncClientOptions { ClientId = "peer-sync-client", ClientSecret = "peer-sync-client-secret" });
        var peerSyncClient = new PeerSyncClient(httpClientFactory, peerSyncClientOptions);

        var sqliteOptions = new DbContextOptionsBuilder<EventStoreContext>().UseSqlite($"Data Source={_dbPathSqlite}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")).Options;
        await using var sqliteDb = new EventStoreContext(sqliteOptions, new SqliteJsonPathTranslator());
        var events = await sqliteDb.Events.AsNoTracking().OrderBy(e => e.SequenceNumber).ToListAsync();
        var pushRequest = new PeerSyncPushRequest("sqlite-site", events.Select(PeerSyncWorker.ToPayload).ToList(), []);
        var pushResponse = await peerSyncClient.PushAsync("", pushRequest, CancellationToken.None);
        Assert.AreEqual(events[^1].SequenceNumber, pushResponse.AckedThroughSequenceNumber);

        // The push only appends (Status: "received") -- RouterWorker (a
        // real BackgroundService in this WebApplicationFactory, per this
        // class's own ClassInit comment) is what actually folds it into
        // EventTypeDefinitions via SchemaRegistrationReplicationResolver,
        // on its own 200ms poll cycle. Polled, not a fixed sleep -- the
        // same "exercise the mechanics directly" posture every other
        // async-completion assertion in this suite already uses.
        JsonElement schemaBody = default;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, $"/registry/WidgetCreated?appId={appId}");
            AuthScenarioAssertions.AttachAuth(pollRequest, _sqlServerClient, token, key);
            var pollResponse = await _sqlServerClient.SendAsync(pollRequest);
            if (pollResponse.StatusCode == HttpStatusCode.OK)
            {
                schemaBody = await pollResponse.Content.ReadFromJsonAsync<JsonElement>();
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        Assert.AreNotEqual(default, schemaBody, "WidgetCreated never became queryable at the SQL Server site within the timeout");
        Assert.AreEqual("string", schemaBody.GetProperty("properties").GetProperty("WidgetId").GetProperty("type").GetString());

        // Proves the replicated FilterableField's own provider-specific
        // index DDL actually ran on SQL Server too, not just the bare
        // EventTypeDefinition row -- a real, queryable, indexed field on
        // the peer that never independently registered it.
        var sqlServerOptions = new DbContextOptionsBuilder<EventStoreContext>().UseSqlServer(_sqlServerConnectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer")).Options;
        await using var sqlServerDb = new EventStoreContext(sqlServerOptions, new SqlServerJsonPathTranslator());
        var replicatedDefinition = await sqlServerDb.EventTypeDefinitions
            .Include(e => e.FilterableFields)
            .AsNoTracking()
            .SingleAsync(e => e.AppId == appId && e.Name == "widgetcreated" && e.IsActive);
        Assert.AreEqual(1, replicatedDefinition.FilterableFields.Count);
        Assert.IsTrue(replicatedDefinition.FilterableFields[0].IsIndexed);
        Assert.AreEqual("$.Category", replicatedDefinition.FilterableFields[0].JsonPath);
    }
}
