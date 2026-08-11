using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Mechanism-Level OpenTelemetry Instrumentation" (docs/08-build-plan.md,
// ADR-088). SQLite-only, deliberately -- nothing about a Meter/
// ActivitySource recording is provider-specific, the same reasoning
// GraphQlFilterPredicateBuilderSqliteTests/CQRS's own single-provider note
// already established for non-provider-specific mechanics. The peer-sync
// outbox depth/age gauge scenario lives in ReplicationHttpSqliteTests.cs
// instead (it needs that file's own two-Host real HTTP fixture to drive
// PeerSyncWorker.SyncOnceWithAsync's real tick, not just PeerSyncReceiver
// directly).
[TestClass]
public class OpenTelemetryInstrumentationSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-otel-{Guid.NewGuid():N}.db");
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

    private static async Task<(WebApplication Backend, string Address)> StartBackendAsync()
    {
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var backend = backendBuilder.Build();
        backend.MapPost("/{**catch-all}", () => Results.Ok());
        await backend.StartAsync();
        var address = backend.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (backend, address);
    }

    [TestMethod]
    public async Task AllOpenTelemetryInstrumentationScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var verifier = new ChainVerificationService(db);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var subscriptions = new WebhookSubscriptionService(db);

        await OpenTelemetryInstrumentationScenarioAssertions.AnAcceptedPublishRecordsRouterFoldLagAndANamedFoldActivity(registry, publish, db, upcastChain);
        await OpenTelemetryInstrumentationScenarioAssertions.AReviewPendingPublishRecordsNoRouterFoldLagAtAll(registry, publish, db, upcastChain);
        await OpenTelemetryInstrumentationScenarioAssertions.VerifyingACleanChainRecordsAVerifiedOutcomeAndANamedActivity(registry, publish, verifier);
        await OpenTelemetryInstrumentationScenarioAssertions.VerifyingATamperedChainRecordsATamperedOutcome(registry, publish, verifier, db);

        using var httpClient = new HttpClient();
        var (backend, address) = await StartBackendAsync();
        await OpenTelemetryInstrumentationScenarioAssertions.AConfirmedWebhookDeliveryRecordsDeliveryLagAndANamedPumpActivity(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, httpClient, address);
        await backend.StopAsync();
    }
}
