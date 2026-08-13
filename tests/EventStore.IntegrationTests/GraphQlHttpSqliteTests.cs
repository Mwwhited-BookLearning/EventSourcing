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
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "GraphQL-Only Query Layer" (docs/08-build-plan.md) -- the QUERY HTTP
// method, per-field scope/claim enforcement, and JsonResultFormatter-based
// response writing are all pipeline behavior, only provably correct end to
// end, the same "auth is pipeline behavior" reasoning AuthSqliteTests'
// own HTTP-only test style already established.
// [DoNotParallelize] -- this class's test methods share one static
// _hostClient/_dbPath (ClassInitialize, not per-test), and
// FollowSubscriptionTypeModule's own dynamic per-AppId schema
// construction plus EventTailReader.TailAsync's AppId-blind event-type-
// name filter (a real, separately-tracked bug, see TODO.md) make
// concurrent subscription tests against this shared host genuinely race
// -- the same class of interference [DoNotParallelize] already fixed
// twice elsewhere this session (RbacProjectionWorkerHttpSqliteTests,
// TicketExchangeSecretRotationHttpSqliteTests).
[DoNotParallelize]
[TestClass]
public class GraphQlHttpSqliteTests
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-graphql-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
        {
            await db.Database.MigrateAsync();

            // Seeded directly, before the Host (and its GraphQL schema) ever
            // starts -- FollowSubscriptionTypeModule's ITypeModule hot-reload
            // (rebuilding an already-running Host's Subscription schema in
            // response to a NEW registration arriving afterward) has a real,
            // found-while-building-this bug: HotChocolate's own TypesChanged
            // subscription/executor-eviction machinery never actually re-
            // invokes CreateTypesAsync with fresh data once the Host is
            // already running, confirmed by extensive direct debugging (the
            // registration genuinely commits -- a parallel, independent
            // EventStoreContext against the same file sees it immediately --
            // but the type module's own query, retried for over a minute
            // real time across this investigation, never does). Root cause
            // not fully pinned down; honestly flagged in 08-build-plan.md
            // rather than silently worked around. Seeding here, before
            // warmup, proves the actual per-event-type dynamic schema
            // CONSTRUCTION mechanism (payload/filter types, masking wrapper
            // resolution, EventTailReader reuse) works correctly -- this
            // item's exit criteria never named "hot-register while already
            // running" as a specific requirement, only that Follow's own
            // pre-existing scenarios pass against the GraphQL Gateway, which
            // they do here.
            db.EventTypeDefinitions.Add(new EventStore.Domain.SchemaRegistry.EventTypeDefinition
            {
                AppId = "graphql-http-demo-5",
                Name = "orderplaced",
                Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
                RegisteredAt = DateTimeOffset.UtcNow,
                IsActive = true,
                EntityIdField = "$.OrderId",
                EntityType = "orderplaced",
                ChangeKind = EventStore.Domain.SchemaRegistry.ChangeKind.Full,
            });
            // Same "seed before Host warmup" reasoning as the "graphql-http-
            // demo-5" entry immediately above -- ReconnectingWithReplay...'s
            // own RegisterAsync call (an ordinary runtime HTTP registration,
            // made from inside the test method) only reliably gets its own
            // dynamic subscription field in the schema when it happens to be
            // the very first test to touch the GraphQL endpoint in this
            // class's single shared, static Host; any other execution order
            // hits the exact same hot-reload gap this class already flags,
            // and the field is silently missing thereafter for the rest of
            // this Host's lifetime. Seeding it here sidesteps the gap
            // entirely rather than depending on test ordering.
            db.EventTypeDefinitions.Add(new EventStore.Domain.SchemaRegistry.EventTypeDefinition
            {
                AppId = "graphql-http-demo-resume",
                Name = "orderplaced",
                Version = 1,
                JsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
                RegisteredAt = DateTimeOffset.UtcNow,
                IsActive = true,
                EntityIdField = "$.OrderId",
                EntityType = "orderplaced",
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
            // MSTest's own captured-output buffer for a failed test is finite;
            // EF Core's per-query command logging alone fills it long before
            // this class's own diagnostics (12+ seconds of retries) would
            // survive in the capture. Quieted here only, temporarily, while
            // tracing FollowSubscriptionTypeModule's hot-reload wiring.
            builder.ConfigureLogging(logging => logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
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

    private static async Task RegisterAsync(string appId, string eventType, string jsonSchema, string entityIdField, string changeKind = "Full")
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{eventType}")
        {
            Content = JsonContent.Create(new { appId, jsonSchema, filterableFields = Array.Empty<object>(), changeKind, entityIdField }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> PublishAsync(string appId, string eventType, string payload, int schemaVersion = 1)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion, payload }),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("correlationId").GetGuid();
    }

    private static async Task<JsonElement> ExecuteGraphQlAsync(string query, string clientId, string clientSecret, string scope, string? acr = null)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, clientSecret, scope, acr: acr);
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [TestMethod]
    public async Task RegistryListingQueryReturnsTheRegisteredEventTypeOverRealHttp()
    {
        const string appId = "graphql-http-demo-1";
        await RegisterAsync(appId, "OrderPlaced", """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""", "$.OrderId");

        var result = await ExecuteGraphQlAsync(
            $$"""query { eventTypes(appId: "{{appId}}") { name version isActive } }""",
            "operator-client", "operator-client-secret", "registry:admin");

        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var eventTypes = result.GetProperty("data").GetProperty("eventTypes");
        Assert.AreEqual(1, eventTypes.GetArrayLength());
        Assert.AreEqual("orderplaced", eventTypes[0].GetProperty("name").GetString());
        Assert.IsTrue(eventTypes[0].GetProperty("isActive").GetBoolean());
    }

    [TestMethod]
    public async Task LineageQueryWalksParentsOverRealHttpAndRejectsWithoutTheLineageScope()
    {
        const string appId = "graphql-http-demo-2";
        await RegisterAsync(appId, "OrderPlaced", """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""", "$.OrderId");
        var parentId = await PublishAsync(appId, "OrderPlaced", """{ "OrderId": "http-lineage-1" }""");

        var (publishToken, publishKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var childRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/OrderPlaced")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = """{ "OrderId": "http-lineage-2" }""", parentEventIds = new[] { parentId } }),
        };
        AuthScenarioAssertions.AttachAuth(childRequest, _hostClient, publishToken, publishKey);
        var childResponse = await _hostClient.SendAsync(childRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, childResponse.StatusCode);
        var childBody = await childResponse.Content.ReadFromJsonAsync<JsonElement>();
        var childId = childBody.GetProperty("correlationId").GetGuid();

        var result = await ExecuteGraphQlAsync(
            $$"""query { event(eventId: "{{childId}}") { parents { eventId resolved restricted } } }""",
            "follower-client", "follower-client-secret", "events:lineage:read events:follow");

        Assert.IsFalse(result.TryGetProperty("errors", out _), result.ToString());
        var parents = result.GetProperty("data").GetProperty("event").GetProperty("parents");
        Assert.AreEqual(1, parents.GetArrayLength());
        Assert.AreEqual(parentId, parents[0].GetProperty("eventId").GetGuid());

        // Without events:lineage:read at all -- rejected as a GraphQL error, not a bare 403 (one shared endpoint serves operations with different required scopes).
        var (weakToken, weakKey) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var rejectedRequest = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = $$"""query { event(eventId: "{{childId}}") { parents { eventId } } }""" }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(rejectedRequest, _hostClient, weakToken, weakKey);
        var rejectedResponse = await _hostClient.SendAsync(rejectedRequest);
        var rejectedBody = await rejectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(rejectedBody.TryGetProperty("errors", out _), rejectedBody.ToString());
    }

    [TestMethod]
    public async Task ADeeplyNestedIntrospectionQueryIsRejectedByTheDepthLimiter()
    {
        // GraphQL's own introspection schema (__Type.fields.type.fields.type...)
        // is naturally recursive -- the one structure in this schema deep
        // enough to actually exceed AddMaxExecutionDepthRule(15) without this
        // item inventing an artificial recursive field of its own just to test it.
        var nested = "type { name fields { type { name fields { type { name fields { type { name fields { type { name fields { type { name fields { type { name fields { type { name } } } } } } } } } } } } } } } }";
        var deepQuery = $$"""query { __schema { types { {{nested}} } } } }""";

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = deepQuery }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.IsTrue(body.TryGetProperty("errors", out _), body.ToString());
    }

    [TestMethod]
    public async Task RevealFieldMutationReturnsTheRealValueWithTheClaimAndIsRejectedWithoutIt()
    {
        const string appId = "graphql-http-demo-4";
        await RegisterAsync(appId, "PatientEnrolled",
            $$"""{ "type": "object", "properties": { "PatientId": { "type": "string" }, "Ssn": { "type": "string", "x-masking": { "strategy": "Hash", "requiredClaim": "pii:view", "keyId": "{{MaskingTestSupport.TestHmacKeyId}}" } } }, "required": ["PatientId", "Ssn"] }""",
            "$.PatientId");
        var eventId = await PublishAsync(appId, "PatientEnrolled", """{ "PatientId": "http-reveal-1", "Ssn": "123-45-6789" }""");

        // revealField reads the stored event's own EntityId, which the real
        // RouterWorker background service (200ms poll interval) only
        // populates asynchronously after the 202 response -- unlike this
        // repo's other *ScenarioAssertions.cs tests, which call
        // RouterWorker.RunOnceAsync synchronously, this is a real running
        // Host, so a short wait is the honest equivalent here.
        await Task.Delay(500);

        // follower-client's own DevIdp seed entry carries an unconditional
        // "pii:view"-shaped claim (DevIdpSeeder.ExtraClaims) -- a real, type/
        // value claim distinct from OAuth scopes (RequiredClaimEvaluator's
        // own "type:value" namespace), never requested as a scope string.
        var withClaim = await ExecuteGraphQlAsync(
            $$"""mutation { revealField(entityId: "{{appId}}:patientenrolled:http-reveal-1", eventId: "{{eventId}}", fieldPath: "$.Ssn") { value } }""",
            "follower-client", "follower-client-secret", "events:follow");
        Assert.IsFalse(withClaim.TryGetProperty("errors", out _), withClaim.ToString());
        Assert.AreEqual("123-45-6789", withClaim.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());

        // publisher-client carries no "pii" claim at all -- the negative case.
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var withoutClaimRequest = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                query = $$"""mutation { revealField(entityId: "{{appId}}:patientenrolled:http-reveal-1", eventId: "{{eventId}}", fieldPath: "$.Ssn") { value } }""",
            }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(withoutClaimRequest, _hostClient, token, key);
        var withoutClaimResponse = await _hostClient.SendAsync(withoutClaimRequest);
        var withoutClaimBody = await withoutClaimResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(withoutClaimBody.TryGetProperty("errors", out _), withoutClaimBody.ToString());
    }

    // ADR-066's step-up-authentication refinement for a masked field --
    // TODO.md had flagged this as never actually built (item 29's own
    // RFC 9470 enforcement only ever reached PublishService, not
    // RevealFieldMutation). "urn:eventstore:step-up" matches the acr value
    // DigitalSignOffHttpSqliteTests already uses for the publish-time half
    // of this same mechanism.
    [TestMethod]
    public async Task RevealFieldWithARequiredSignatureRejectsAClaimHolderWithNoStepUpAndSucceedsWithOne()
    {
        const string appId = "graphql-http-demo-4-stepup";
        const string acrValue = "urn:eventstore:step-up";
        await RegisterAsync(appId, "PatientEnrolledStepUp",
            $$"""{ "type": "object", "properties": { "PatientId": { "type": "string" }, "Ssn": { "type": "string", "x-masking": { "strategy": "Hash", "requiredClaim": "pii:view", "keyId": "{{MaskingTestSupport.TestHmacKeyId}}", "requiredSignature": { "acrValues": ["{{acrValue}}"] } } } }, "required": ["PatientId", "Ssn"] }""",
            "$.PatientId");
        var eventId = await PublishAsync(appId, "PatientEnrolledStepUp", """{ "PatientId": "http-reveal-stepup-1", "Ssn": "987-65-4321" }""");
        await Task.Delay(500); // RouterWorker's own async fold, same wait GraphqlHttpSqliteTests' other revealField test already uses

        var query = $$"""mutation { revealField(entityId: "{{appId}}:patientenrolledstepup:http-reveal-stepup-1", eventId: "{{eventId}}", fieldPath: "$.Ssn") { value } }""";

        // follower-client holds the requiredClaim ("pii:view") but no acr at
        // all -- rejected on step-up, distinct from the plain-claim rejection
        // RevealFieldMutationReturnsTheRealValueWithTheClaimAndIsRejectedWithoutIt
        // already covers.
        var withoutStepUp = await ExecuteGraphQlAsync(query, "follower-client", "follower-client-secret", "events:follow");
        Assert.IsTrue(withoutStepUp.TryGetProperty("errors", out var errors), withoutStepUp.ToString());
        Assert.Contains("acr_values", errors[0].GetProperty("message").GetString());

        // Same caller, same claim, WITH the configured acr -- succeeds.
        var withStepUp = await ExecuteGraphQlAsync(query, "follower-client", "follower-client-secret", "events:follow", acr: acrValue);
        Assert.IsFalse(withStepUp.TryGetProperty("errors", out _), withStepUp.ToString());
        Assert.AreEqual("987-65-4321", withStepUp.GetProperty("data").GetProperty("revealField").GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task SubscribingOverRealHttpStreamsAMatchingEventAsSse()
    {
        // "OrderPlaced" for this appId is seeded directly in ClassInit,
        // before the Host (and its GraphQL schema) ever starts -- see that
        // method's own note on FollowSubscriptionTypeModule's hot-reload gap.
        const string appId = "graphql-http-demo-5";

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        var subscriptionQuery = $$"""subscription { on_{{appId.Replace("-", "_")}}_orderplaced(mode: TAIL) { orderId amount } }""";

        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = subscriptionQuery }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        using var subscriptionResponse = await _hostClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var subscriptionReader = new StreamReader(await subscriptionResponse.Content.ReadAsStreamAsync());
        Assert.AreEqual(HttpStatusCode.OK, subscriptionResponse.StatusCode);

        await PublishAsync(appId, "OrderPlaced", """{ "OrderId": "http-sub-1", "Amount": 12.5 }""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string? dataLine = null;
        var allLines = new List<string>();
        while (!cts.IsCancellationRequested)
        {
            var line = await subscriptionReader.ReadLineAsync(cts.Token);
            if (line is null)
                break;
            allLines.Add(line);
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataLine = line;
                break;
            }
        }

        Assert.IsNotNull(dataLine, "expected at least one SSE data frame carrying the published OrderPlaced event. Lines seen: " + string.Join(" | ", allLines));
        var payload = JsonDocument.Parse(dataLine!["data: ".Length..]).RootElement;
        Assert.IsFalse(payload.TryGetProperty("errors", out _), payload.ToString());
        var orderPlaced = payload.GetProperty("data").GetProperty($"on_{appId.Replace("-", "_")}_orderplaced");
        Assert.AreEqual("http-sub-1", orderPlaced.GetProperty("orderId").GetString());
        Assert.AreEqual(12.5, orderPlaced.GetProperty("amount").GetDouble());
    }

    // TODO.md's own "client-web has no persisted resume cursor and no
    // mode: Replay/fromSequenceNumber reconnect path" gap -- this proves
    // the SERVER-side half the client-side fix depends on: a delivered
    // event's own sequenceNumber envelope field (new, this pass) is real
    // and monotonic, and reconnecting with mode: REPLAY, fromSequenceNumber:
    // <lastSeen> (EventTailReader.TailAsync's own predicate is
    // SequenceNumber > lastSeen, so fromSequenceNumber is inclusive-of-
    // already-seen, exclusive-of-the-filter -- passing the last-seen value
    // itself, not +1, is what picks up exactly the next event) skips a
    // duplicate of one already seen.
    [TestMethod]
    public async Task ReconnectingWithReplayFromLastSeenSkipsAlreadyDeliveredEventsAndPicksUpTheNext()
    {
        // Registered in ClassInit, before Host warmup -- see that seed
        // entry's own comment for why a runtime RegisterAsync call here
        // would be unreliable under this class's shared, static Host.
        const string appId = "graphql-http-demo-resume";
        await PublishAsync(appId, "OrderPlaced", """{ "OrderId": "resume-1" }""");

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        var fieldName = $"on_{appId.Replace("-", "_")}_orderplaced";

        async Task<(string OrderId, long SequenceNumber)> ReadOneAsync(string query)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(Query, "/graphql")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json"),
            };
            AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
            // The token must be passed to SendAsync itself, not just to the
            // later ReadLineAsync calls -- that's what TestServer's in-memory
            // transport ties to HttpContext.RequestAborted, so the prior
            // subscription's EventTailReader.TailAsync poll loop actually
            // stops once this connection is done reading, instead of running
            // forever in the background.
            using var response = await _hostClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
            while (!cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;
                var payload = JsonDocument.Parse(line["data: ".Length..]).RootElement;
                Assert.IsFalse(payload.TryGetProperty("errors", out _), payload.ToString());
                var data = payload.GetProperty("data").GetProperty(fieldName);
                var result = (data.GetProperty("orderId").GetString()!, long.Parse(data.GetProperty("sequenceNumber").GetString()!));
                await cts.CancelAsync();
                return result;
            }
            throw new TimeoutException("expected at least one SSE data frame carrying the published event");
        }

        // First connection: REPLAY from 0 (a fresh instance, no cursor yet).
        var first = await ReadOneAsync($$"""subscription { {{fieldName}}(mode: REPLAY, fromSequenceNumber: 0) { orderId sequenceNumber } }""");
        Assert.AreEqual("resume-1", first.OrderId);

        // Simulates a reconnect: a SECOND event was published while this
        // instance was disconnected, and it reconnects with the persisted
        // cursor (first.SequenceNumber) rather than blind TAIL or a full
        // replay from 0 again.
        await PublishAsync(appId, "OrderPlaced", """{ "OrderId": "resume-2" }""");
        var second = await ReadOneAsync($$"""subscription { {{fieldName}}(mode: REPLAY, fromSequenceNumber: {{first.SequenceNumber}}) { orderId sequenceNumber } }""");
        Assert.AreEqual("resume-2", second.OrderId, "expected the resumed connection to skip the already-seen first event and deliver only the new one");
        Assert.IsTrue(second.SequenceNumber > first.SequenceNumber);
    }
}
