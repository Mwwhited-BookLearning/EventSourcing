using System.Security.Claims;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.Contrib.Simmy;
using Polly.Contrib.Simmy.Outcomes;

namespace EventStore.UnitTests;

// ADR-063 -- Polly+Simmy in-process fault injection for "whether the
// durable outbox/inbox actually resumes correctly after a simulated
// crash." A true process-kill test needs the deferred Testcontainers+
// Toxiproxy tier (this ADR's own named escalation, not built this pass);
// what IS cheaply testable in-process is the specific, real failure mode
// that tier would also have to cover eventually: a durable write commits
// successfully, but the CALLER never learns of it (the connection dies,
// the process crashes, the response is lost in flight) -- a genuinely
// common distributed-systems failure, and exactly what ADR-011's
// EventId-based idempotency exists to make safe to retry.
[TestClass]
public class PublishCrashRecoveryFaultInjectionTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-unittests-crash-recovery-{Guid.NewGuid():N}.db");
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

    [TestMethod]
    public async Task ARetryAfterTheCallerNeverLearnsOfADurablySucceededPublishNeverDuplicatesTheEvent()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), new CelUpcastExpressionEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        const string appId = "crash-recovery-demo-1";
        await registry.RegisterAsync("WidgetCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.WidgetId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var eventId = Guid.NewGuid();
        var request = new PublishEventRequest(appId, 1, """{ "WidgetId": "widget-1", "Name": "Original" }""", null, eventId);

        // Attempt 1: the REAL, durable write -- this genuinely commits,
        // exactly as it would in production, before anything simulates a
        // crash.
        var firstAttemptResult = (PublishResult.Accepted)await publish.PublishAsync("WidgetCreated", request, ClaimsPrincipal, CancellationToken.None);

        // Simulate the crash: the caller never actually receives the
        // response above (a dropped connection, a process restart between
        // the commit and the HTTP response being written) -- Simmy
        // injects the fault on a SEPARATE delegate representing "deliver
        // the response," never on the write itself, so the durable data
        // this test just asserted committed above is completely
        // unaffected by what happens next.
        var chaosPolicy = MonkeyPolicy.InjectExceptionAsync(with => with
            .Fault(new IOException("simulated: connection dropped after commit, before the caller received this response"))
            .InjectionRate(1.0)
            .Enabled(true));
        await Assert.ThrowsExactlyAsync<IOException>(() => chaosPolicy.ExecuteAsync(() => Task.FromResult<object?>(firstAttemptResult)));

        // Attempt 2 (the caller's own retry, having never seen attempt
        // 1's own success) -- SAME EventId, no fault this time.
        var retryResult = (PublishResult.Accepted)await publish.PublishAsync("WidgetCreated", request, ClaimsPrincipal, CancellationToken.None);

        // ADR-011's idempotency: the retry must be recognized as a
        // replay of the SAME event, not a second, duplicate write.
        Assert.AreEqual(firstAttemptResult.SequenceNumber, retryResult.SequenceNumber, "a retry after a lost response must resolve to the SAME sequence number, never a new one");
        var storedCount = await db.Events.CountAsync(e => e.EventId == eventId);
        Assert.AreEqual(1, storedCount, "the durable write must never be duplicated by a safe retry");
    }

    // The mirror case: a genuinely DIFFERENT publish reusing the SAME
    // EventId with DIFFERENT content is a real conflict (ADR-011), not a
    // safe replay -- the crash-recovery guarantee above must not be so
    // broad that it also papers over an actual EventId collision between
    // two unrelated publishes.
    [TestMethod]
    public async Task ARetryWithTheSameEventIdButGenuinelyDifferentContentIsReportedAsAConflictNotASilentReplay()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), new CelUpcastExpressionEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        const string appId = "crash-recovery-demo-2";
        await registry.RegisterAsync("WidgetCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.WidgetId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var eventId = Guid.NewGuid();
        await publish.PublishAsync("WidgetCreated",
            new PublishEventRequest(appId, 1, """{ "WidgetId": "widget-2", "Name": "Original" }""", null, eventId),
            ClaimsPrincipal, CancellationToken.None);

        var chaosPolicy = MonkeyPolicy.InjectExceptionAsync(with => with
            .Fault(new IOException("simulated crash"))
            .InjectionRate(1.0)
            .Enabled(true));
        await Assert.ThrowsExactlyAsync<IOException>(() => chaosPolicy.ExecuteAsync(() => Task.FromResult<object?>(null)));

        var conflictingRetry = await publish.PublishAsync("WidgetCreated",
            new PublishEventRequest(appId, 1, """{ "WidgetId": "widget-2", "Name": "Genuinely Different" }""", null, eventId),
            ClaimsPrincipal, CancellationToken.None);

        Assert.IsInstanceOfType<PublishResult.Conflict>(conflictingRetry, "the same EventId with genuinely different content must never be silently treated as a safe replay");
    }

    private static readonly ClaimsPrincipal ClaimsPrincipal = new(new ClaimsIdentity());
}
