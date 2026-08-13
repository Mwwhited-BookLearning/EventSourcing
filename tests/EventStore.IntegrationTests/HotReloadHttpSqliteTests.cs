extern alias DevIdpAssembly;

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

// FollowSubscriptionTypeModule's own header comment has the full account
// of this item's history: an EARLIER session's claim that hot-registering
// a new event type against an already-running Host "never" makes its
// Subscription field appear without a restart was a misdiagnosis -- this
// test proves the common case directly, rather than by reasoning about
// HotChocolate's own internals: a type registered AFTER this Host has
// already started becomes queryable, and a real Subscription against it
// delivers a real published event, with no restart and no extra code
// beyond what already existed (TypesChanged -> HotChocolate's own
// TypeModuleChangeMonitor -> RequestExecutorManager.EvictExecutor).
// A real, narrower gap DOES still exist under concurrent overlapping
// registrations (TODO.md tracks it) -- not exercised here, since it's a
// HotChocolate-internal failure mode this class's own code cannot safely
// work around (see FollowSubscriptionTypeModule's header comment for the
// full reasoning on why not).
[TestClass]
public class HotReloadHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-hotreload-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
        {
            await db.Database.MigrateAsync();
            // At least one event type must be active before the Host
            // starts -- this class's own point is a SECOND type registered
            // AFTER warmup, not proving the (already covered elsewhere)
            // dynamic schema construction mechanism itself from zero.
            db.EventTypeDefinitions.Add(new EventStore.Domain.SchemaRegistry.EventTypeDefinition
            {
                AppId = "hotreload-demo-seed",
                Name = "seedevent",
                Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
                RegisteredAt = DateTimeOffset.UtcNow,
                IsActive = true,
                EntityIdField = "$.Id",
                EntityType = "seedevent",
                ChangeKind = EventStore.Domain.SchemaRegistry.ChangeKind.Full,
            });
            await db.SaveChangesAsync();
        }

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

    private static async Task RegisterAsync(string appId, string eventType, string jsonSchema, string entityIdField)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{eventType}")
        {
            Content = JsonContent.Create(new { appId, jsonSchema, filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(System.Net.HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> PublishAsync(string appId, string eventType, string payload)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    [TestMethod]
    public async Task ARealSubscriptionConnectionActuallyReceivesAnEventOnAHotRegisteredType()
    {
        await RegisterAsync("hotreload-demo-new", "HotRegisteredForSub", """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""", "$.Id");
        await PublishAsync("hotreload-demo-new", "HotRegisteredForSub", """{ "Id": "hot-1" }""");
        await AssertSubscriptionDeliversAsync("hotreload-demo-new", "HotRegisteredForSub", "hot-1");
    }

    // EntityQueryTypeModule's own header/field-building comments have the
    // full account: every brand-new AppId's first-ever registration also
    // bootstraps that AppId's own reserved SchemaRegisteredEventType, whose
    // JSON schema declares a top-level "Version" property. Before the fix,
    // that property's own GraphQL field name ("version") collided with
    // BuildEntityEnvelopeFields()'s own hardcoded "version" envelope field,
    // throwing HotChocolate.SchemaException on the very next rebuild --
    // silently swallowed by RequestExecutorManager's own consumer loop, and
    // fatal to it: TypeModuleChangeMonitor gets disposed (unsubscribed) on
    // that failure with nothing left to ever re-subscribe it, permanently
    // killing hot-reload for every AppId from that point forward. A single
    // hot registration (the test above) could still accidentally succeed on
    // a lucky race (its own rebuild beating the bootstrap event's), so this
    // test's real point is TWO SEQUENTIAL, independent new AppIds: if the
    // first one's own bootstrap permanently broke eviction, the second's
    // field would never appear no matter how long this waits.
    [TestMethod]
    public async Task ASecondIndependentHotRegistrationStillWorksAfterAnEarlierAppIdsOwnSchemaBootstrap()
    {
        await RegisterAsync("hotreload-demo-first", "FirstHotType", """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""", "$.Id");
        await PublishAsync("hotreload-demo-first", "FirstHotType", """{ "Id": "first-1" }""");
        await AssertSubscriptionDeliversAsync("hotreload-demo-first", "FirstHotType", "first-1");

        await RegisterAsync("hotreload-demo-second", "SecondHotType", """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""", "$.Id");
        await PublishAsync("hotreload-demo-second", "SecondHotType", """{ "Id": "second-1" }""");
        await AssertSubscriptionDeliversAsync("hotreload-demo-second", "SecondHotType", "second-1");
    }

    private static async Task AssertSubscriptionDeliversAsync(string appId, string eventType, string expectedId)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        var fieldName = $"on_{appId.Replace('-', '_')}_{eventType.ToLowerInvariant()}";
        var query = $$"""subscription { {{fieldName}}(mode: REPLAY, fromSequenceNumber: 0) { id } }""";

        // The rebuild HotChocolate's own TypesChanged -> EvictExecutor ->
        // background channel consumer triggers is asynchronous and its
        // own duration is not bounded -- under this suite's own heavy
        // aggregate load (many concurrent Hosts, thread-pool/GC
        // contention), it can take longer than it does in isolation. A
        // single, non-retrying subscribe attempt right after RegisterAsync
        // returns can race a rebuild still in flight and get an immediate
        // GraphQL validation error (the field genuinely doesn't exist
        // YET) rather than a real SSE stream -- retried here the same way
        // this session's own established pattern handles every other
        // async-completion race (BatchPublishHttpSqliteTests' own
        // RouterWorker-tick wait, LineageExportHttpSqliteTests'
        // ExportLineageAsync retry), not assumed to always resolve
        // instantly.
        string? dataLine = null;
        for (var attempt = 0; attempt < 67 && dataLine is null; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(150);

            using var request = new HttpRequestMessage(new HttpMethod("QUERY"), "/graphql")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json"),
            };
            AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _hostClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
            while (!cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    dataLine = line;
                    break;
                }
            }
        }

        Assert.IsNotNull(dataLine, $"expected {fieldName}'s own subscription to actually deliver the published event within ~10s, with no process restart");
        var payload = JsonDocument.Parse(dataLine!["data: ".Length..]).RootElement;
        Assert.IsFalse(payload.TryGetProperty("errors", out _), payload.ToString());
        Assert.AreEqual(expectedId, payload.GetProperty("data").GetProperty(fieldName).GetProperty("id").GetString());
    }
}
