using System.Diagnostics;
using System.Text.Json.Nodes;
using EventStore.Inbox;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using EventStore.Webhooks;
using EventStore.WorkerWakeSignal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// TODO.md's "ADR-095... wired into RouterWorker only" follow-up, now closed:
// proves each of the 5 newly-wired notify call sites (PublishService for
// derivation/expectedresponse/peersync; RouterWorker for webhookoutbox;
// TelemetrySampleWriter for channelderivation) actually signals its own
// topic, the same "exercise the mechanics directly" pattern
// WorkerWakeSignalSqliteTests already established for RouterWorker's own
// "router" topic and PublishService's own notify call. One combined test
// method, not five -- these all share one EventStoreContext/one
// SqliteWorkerWakeSignal instance, and MSTest's own method-level
// parallelism would otherwise race them the same way it already did for
// WorkerWakeSignalSqlServerTests (that class's own comment explains why).
[TestClass]
public class WakeSignalExtendedWorkersSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-wakesignal-ext-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    // A minimal, real IPayloadMasker -- constructing the production
    // PayloadMasker directly needs a full DI graph (IServiceProvider,
    // IRedactorProvider, ErasureKeyService) this test has no other reason
    // to stand up; RouterWorker's own webhookoutbox notify only depends on
    // payloadMasker being non-null, never on what it actually returns.
    private sealed class NoOpPayloadMasker : IPayloadMasker
    {
        public Task<JsonNode?> MaskAsync(JsonNode schema, JsonNode? payload, string? entityId, Func<string, bool> hasClaim, CancellationToken ct = default) =>
            Task.FromResult(payload);
    }

    [TestMethod]
    public async Task AllExtendedWakeSignalScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var wakeSignal = new SqliteWorkerWakeSignal(db);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), wakeSignal: wakeSignal);

        await registry.RegisterAsync("Widget", new RegisterEventTypeRequest(
            AppId: "wake-ext-1", JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        // A publish signals all three of derivation/expectedresponse/
        // peersync together -- every new event is a candidate for all
        // three (PublishService.cs's own comment explains why there's no
        // cheaper-to-check condition than "a new event exists at all").
        var stopwatch = Stopwatch.StartNew();
        var derivationWait = wakeSignal.WaitForWakeAsync(WakeSignalTopics.Derivation, TimeSpan.FromSeconds(5), CancellationToken.None);
        var expectedResponseWait = wakeSignal.WaitForWakeAsync(WakeSignalTopics.ExpectedResponse, TimeSpan.FromSeconds(5), CancellationToken.None);
        var peerSyncWait = wakeSignal.WaitForWakeAsync(WakeSignalTopics.PeerSync, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50); // let every wait actually start listening before publishing

        var published = (PublishResult.Accepted)await publish.PublishAsync(
            "Widget", new PublishEventRequest("wake-ext-1", 1, """{ "Id": "widget-1" }""", null, null), TestClaimsPrincipal.None);
        Assert.AreEqual("received", published.Status);

        await Task.WhenAll(derivationWait, expectedResponseWait, peerSyncWait);
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"expected derivation/expectedresponse/peersync to all wake near-immediately from one publish, took {stopwatch.Elapsed}");

        // RouterWorker notifies webhookoutbox once its own tick actually
        // processes something, with a non-null payloadMasker -- narrower
        // than PublishService's "every new event" above, matching that
        // this topic's own worker only ever has something to do once an
        // event has FOLDED, not merely been appended.
        var upcastChain = UpcastingTestSupport.CreateChain();
        var webhookStopwatch = Stopwatch.StartNew();
        var webhookWait = wakeSignal.WaitForWakeAsync(WebhookOutboxPump.Topic, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: new NoOpPayloadMasker(), wakeSignal: wakeSignal);
        await webhookWait;
        webhookStopwatch.Stop();
        Assert.IsTrue(webhookStopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"expected webhookoutbox to wake near-immediately once RouterWorker's own tick folded the widget event, took {webhookStopwatch.Elapsed}");

        var foldedWidget = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == published.CorrelationId);
        Assert.AreEqual("applied", foldedWidget.Status, "sanity check -- the tick above genuinely folded something, it isn't an empty tick that happened to still notify");

        // TelemetrySampleWriter notifies channelderivation from a
        // completely separate write path (ADR-031's own data plane, never
        // routed through PublishService/RouterWorker at all).
        var channelRegistry = new ChannelRegistryService(db);
        var ingestOptions = Options.Create(new TelemetryIngestOptions());
        var writer = new TelemetrySampleWriter(db, registry, publish, ingestOptions, wakeSignal);
        await channelRegistry.RegisterAsync("wake-ext-channel-1", new RegisterChannelRequest(
            AppId: "wake-ext-1", EntityId: "patient:1", ContentKind: "RawScalar", SampleType: "Float64",
            MimeType: null, SampleIntervalMicros: 4000, Origin: "Origin",
            ThreadId: null, SourceChannelIds: null, TransformKind: null, RequiredReadClaim: null));

        var channelStopwatch = Stopwatch.StartNew();
        var channelWait = wakeSignal.WaitForWakeAsync(ChannelDerivationWorker.Topic, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        var ingestResult = await writer.IngestAsync("wake-ext-channel-1", new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-08-12T10:00:00Z"), SampleIntervalMicros: 4000,
            Values: [0.1, 0.2], Samples: null));
        Assert.IsInstanceOfType<IngestSamplesResult.Accepted>(ingestResult);
        await channelWait;
        channelStopwatch.Stop();
        Assert.IsTrue(channelStopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"expected channelderivation to wake near-immediately from one ingest, took {channelStopwatch.Elapsed}");
    }
}
