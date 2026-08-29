using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

// Direct regression coverage for two real bugs, both found by actually
// running the real AppHost against real Postgres, neither caught by any of
// this project's other Postgres tests because none of them construct their
// DbContext with EnableRetryOnFailure the way EventStore.Host.Postgres's
// real Program.cs does -- every test below deliberately mirrors that exact
// configuration instead of PublishPostgresTests.cs's own plain UseNpgsql,
// specifically so it exercises the SAME retrying execution strategy both
// real bugs needed to reproduce.
//
// Bug 1: EventAppender.AppendAsync/AccessLogAppender.AppendAsync/
// SchemaRegistryService.RegisterAsync all called db.Database.
// BeginTransactionAsync(...) directly -- EF throws "The configured
// execution strategy ... does not support user-initiated transactions" the
// moment a retrying strategy is active, unless the whole retryable unit
// runs inside db.Database.CreateExecutionStrategy().ExecuteAsync(...). Any
// ONE of the single-publish/single-registration/single-append tests below
// would have failed outright before that fix.
//
// Bug 2: fixing bug 1 by naively wrapping each method's existing body in
// ExecuteAsync introduced a SECOND, more dangerous bug -- silent hash-chain
// corruption, not a thrown exception -- because the entity each method
// appends (StoredEvent/AccessLogEntry) is constructed ONCE, by the caller,
// and reused across every retry attempt. A retry whose earlier attempt's
// transaction aborted AFTER a real (but since-rolled-back) INSERT can leave
// EF's change tracker believing that entity's identity-generated
// SequenceNumber was already assigned; a bare re-Add() on the next attempt
// then skips re-generating it, and ChainHash gets computed from a stale
// value that no longer matches the row actually being inserted. Only the
// concurrent test below (genuinely forcing a real 40001) can reproduce
// this -- a single, uncontended publish never hits the retry path at all.
//
// Bug 3: fixing bugs 1 and 2 still left a real, reported AppHost startup
// crash -- 40001 IS now correctly classified as transient, but the retry
// BUDGET (maxRetryCount: 10, maxRetryDelay: 2s) can genuinely be exhausted
// under sustained multi-writer load, propagating the exact same exception
// the caller sees regardless of how correct the retry logic itself is.
// SustainedConcurrentLoadFromMultipleWritersNeverExhaustsTheRetryBudget
// below reproduces this directly (16 concurrent writers for 15s fails
// reliably at maxRetryCount: 10, passes reliably -- 3 repeated runs -- at
// 20, which EventStore.Host.Postgres/Program.cs now uses).
[TestClass]
public class RetryOnFailurePostgresTests
{
    private static PostgreSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup() => await _container.DisposeAsync();

    // Mirrors EventStore.Host.Postgres/Program.cs's own EnableRetryOnFailure
    // call exactly, "40001" included -- see that file's own comment for why.
    private static EventStoreContext CreateContext(Action? onRetry = null)
    {
        var builder = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(_container.GetConnectionString(), x => x
                .MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")
                .EnableRetryOnFailure(maxRetryCount: 20, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: ["3D000", "40001"]));
        // docs/bugs/framework/database/postgres-routine-40001-serialization-noise.md's
        // own regression test needs to observe EF's own retry diagnostic
        // event directly -- an ILoggerProvider filtering on
        // CoreEventId.ExecutionStrategyRetrying is the stable, always-
        // available public hook for that.
        if (onRetry is not null)
            builder = builder.UseLoggerFactory(LoggerFactory.Create(b => b.AddProvider(new RetryCountingLoggerProvider(onRetry))));
        return new EventStoreContext(builder.Options, new PostgresJsonPathTranslator());
    }

    private sealed class RetryCountingLoggerProvider(Action onRetry) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RetryCountingLogger(onRetry);
        public void Dispose() { }

        private sealed class RetryCountingLogger(Action onRetry) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (eventId.Id == CoreEventId.ExecutionStrategyRetrying.Id) onRetry();
            }
        }
    }

    private const string RetryTestEventSchema = """
        { "type": "object", "properties": { "Marker": { "type": "string" } }, "required": ["Marker"] }
        """;

    [TestMethod]
    public async Task ASingleSchemaRegistrationSucceedsUnderARetryingExecutionStrategy()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

        var result = await registry.RegisterAsync("RetryRegistrationTest", new RegisterEventTypeRequest(
            AppId: "retry-registration-demo", JsonSchema: RetryTestEventSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Marker",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
    }

    [TestMethod]
    public async Task ASingleOrdinaryPublishSucceedsUnderARetryingExecutionStrategy()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());

        await registry.RegisterAsync("RetryPublishTest", new RegisterEventTypeRequest(
            AppId: "retry-publish-demo", JsonSchema: RetryTestEventSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Marker",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var result = await publish.PublishAsync("RetryPublishTest", new PublishEventRequest(
            AppId: "retry-publish-demo", SchemaVersion: 1, Payload: """{ "Marker": "m-1" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
    }

    [TestMethod]
    public async Task ASingleAccessLogAppendSucceedsUnderARetryingExecutionStrategy()
    {
        using var db = CreateContext();

        await AccessLogAppender.AppendAsync(db, "reader-retry-test", "Authoritative", null, "Authoritative", "resource-retry-test", "query");

        var stored = await db.AccessLogEntries.SingleOrDefaultAsync(e => e.ResourceRef == "resource-retry-test");
        Assert.IsNotNull(stored);
        Assert.IsGreaterThan(0, stored.SequenceNumber);
    }

    [TestMethod]
    public async Task ManyConcurrentPublishesAgainstTheSameAppIdAllSucceedDespiteRealSerializationConflicts()
    {
        const int concurrency = 30;
        const string appId = "concurrent-publish-demo";

        using (var setupDb = CreateContext())
        {
            var setupRegistry = new SchemaRegistryService(setupDb, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            await setupRegistry.RegisterAsync("ConcurrentEvent", new RegisterEventTypeRequest(
                AppId: appId, JsonSchema: RetryTestEventSchema, FilterableFields: [],
                ChangeKind: "Full", EntityIdField: "$.Marker",
                ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        }

        // Each concurrent publisher gets its OWN DbContext/PublishService,
        // matching real production shape (one scoped DbContext per request/
        // tick) -- sharing a single DbContext across concurrent Tasks would
        // throw for an entirely different, unrelated reason (EF's own
        // "a second operation started on this context before a previous
        // operation completed" guard) and would prove nothing about the
        // real bug this test targets.
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            using var db = CreateContext();
            var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
            return await publish.PublishAsync("ConcurrentEvent", new PublishEventRequest(
                AppId: appId, SchemaVersion: 1, Payload: $$"""{ "Marker": "m-{{i}}" }""",
                ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
            Assert.IsInstanceOfType<PublishResult.Accepted>(result, $"expected Accepted, got {result} -- a real serialization conflict should retry away, never surface to the caller");

        // Proves the retry path didn't just avoid throwing, but left a
        // genuinely valid chain behind -- a retried EventAppender.AppendAsync
        // attempt that redid only PART of its work (e.g. re-inserted the row
        // but computed ChainHash/LogicalClock against stale prior-tail state
        // from a discarded earlier attempt) would surface here, not as an
        // exception. This is exactly the assertion that failed before this
        // pass's second fix (detach + reset SequenceNumber before every
        // retry attempt), confirmed by actually reverting that fix locally
        // and re-running this test, not merely inferred.
        using var verifyDb = CreateContext();
        var maxSequenceNumber = await verifyDb.Events.Where(e => e.AppId == appId).MaxAsync(e => e.SequenceNumber);
        var verifier = new ChainVerificationService(verifyDb);
        var verification = await verifier.VerifyAsync(maxSequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(verification, "a retried append must never leave the hash chain internally inconsistent");
    }

    // docs/bugs/framework/database/postgres-routine-40001-serialization-noise.md --
    // reproduces the actual reported bug (routine, visible 40001 "could not
    // serialize access" noise under real concurrency), not just "publishes
    // still eventually succeed" (the test above already covered that, and
    // passed even before this fix -- the retries were always correct, the
    // CONFLICT FREQUENCY was the actual defect). Counts EF's own
    // CoreEventId.ExecutionStrategyRetrying diagnostic event directly via a
    // custom ILoggerProvider, the stable public hook for that.
    [TestProperty("BugReport", "docs/bugs/framework/database/postgres-routine-40001-serialization-noise.md")]
    [TestMethod]
    public async Task ConcurrentPublishesAgainstTheSameTailNeverTriggerAnEF40001Retry()
    {
        const int concurrency = 30;
        const string appId = "advisory-lock-demo";
        var retryCount = 0;
        void CountRetries() => Interlocked.Increment(ref retryCount);

        using (var setupDb = CreateContext())
        {
            var setupRegistry = new SchemaRegistryService(setupDb, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            await setupRegistry.RegisterAsync("AdvisoryLockEvent", new RegisterEventTypeRequest(
                AppId: appId, JsonSchema: RetryTestEventSchema, FilterableFields: [],
                ChangeKind: "Full", EntityIdField: "$.Marker",
                ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

            // Publish ONE event first, deliberately -- this test targets the
            // steady-state "many concurrent appenders against an established
            // tail" case; the empty-table genesis race is a real, DIFFERENT
            // case with its own dedicated test right below, since the two
            // failure modes are not equivalent (see AppendSerializationLock's
            // own class comment on why a FOR-UPDATE-based fix was rejected
            // specifically because it doesn't cover the genesis case, unlike
            // the advisory-lock fix actually shipped, which covers both).
            var setupPublish = new PublishService(setupDb, setupRegistry, new PostgresUniqueConstraintViolationDetector());
            await setupPublish.PublishAsync("AdvisoryLockEvent", new PublishEventRequest(
                AppId: appId, SchemaVersion: 1, Payload: """{ "Marker": "seed" }""", ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        }

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            using var db = CreateContext(CountRetries);
            var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
            return await publish.PublishAsync("AdvisoryLockEvent", new PublishEventRequest(
                AppId: appId, SchemaVersion: 1, Payload: $$"""{ "Marker": "m-{{i}}" }""",
                ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
            Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        Assert.AreEqual(0, retryCount,
            "the advisory lock (AppendSerializationLock) plus Read Committed isolation should serialize concurrent appenders BEFORE Postgres ever needs to abort one -- any retry here means a real 40001 conflict still occurred");

        using var verifyDb = CreateContext();
        var maxSequenceNumber = await verifyDb.Events.Where(e => e.AppId == appId).MaxAsync(e => e.SequenceNumber);
        var verifier = new ChainVerificationService(verifyDb);
        var verification = await verifier.VerifyAsync(maxSequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(verification, "the fix must never leave the hash chain internally inconsistent, the same correctness bar the original Serializable-only implementation already had to meet");
    }

    // The empty-table race AppendSerializationLock's own class comment
    // names as the reason a `SELECT ... FOR UPDATE`-on-the-tail-row
    // alternative was rejected: with NO prior row to lock, that approach
    // has nothing to serialize the very first concurrent inserts against.
    // pg_advisory_xact_lock's key is a fixed integer, not a data row, so it
    // closes this gap too -- this test is what actually proves that, not
    // just the reasoning in a comment.
    [TestMethod]
    public async Task ConcurrentPublishesAgainstFreshlyRegisteredEventTypeWithNoPriorEventsNeverCorruptTheChain()
    {
        const int concurrency = 20;
        const string appId = "genesis-race-demo";

        using (var setupDb = CreateContext())
        {
            var setupRegistry = new SchemaRegistryService(setupDb, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            await setupRegistry.RegisterAsync("GenesisRaceEvent", new RegisterEventTypeRequest(
                AppId: appId, JsonSchema: RetryTestEventSchema, FilterableFields: [],
                ChangeKind: "Full", EntityIdField: "$.Marker",
                ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        }
        // No seed publish here, deliberately -- these `concurrency` publishes
        // are the very FIRST events ever written to this Postgres database's
        // Events table across ALL AppIds (a fresh container per test class,
        // ClassInitialize migrates but never publishes), so every one of
        // them races to compute its own ChainHash against Genesis.

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            using var db = CreateContext();
            var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
            return await publish.PublishAsync("GenesisRaceEvent", new PublishEventRequest(
                AppId: appId, SchemaVersion: 1, Payload: $$"""{ "Marker": "g-{{i}}" }""",
                ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        });

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        using var verifyDb = CreateContext();
        var maxSequenceNumber = await verifyDb.Events.MaxAsync(e => e.SequenceNumber);
        var verifier = new ChainVerificationService(verifyDb);
        var verification = await verifier.VerifyAsync(maxSequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(verification,
            "a genesis-race chain corruption would surface here as a chain-hash mismatch, not as a thrown exception -- exactly the class of bug a FOR-UPDATE-on-the-tail-row fix would NOT have caught");
    }

    [TestMethod]
    public async Task SustainedConcurrentLoadFromMultipleWritersNeverExhaustsTheRetryBudget()
    {
        const int writers = 16;
        var duration = TimeSpan.FromSeconds(15);
        const string appId = "sustained-load-demo";

        using (var setupDb = CreateContext())
        {
            var setupRegistry = new SchemaRegistryService(setupDb, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            await setupRegistry.RegisterAsync("SustainedEvent", new RegisterEventTypeRequest(
                AppId: appId, JsonSchema: RetryTestEventSchema, FilterableFields: [],
                ChangeKind: "Full", EntityIdField: "$.Marker",
                ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        }

        var deadline = DateTimeOffset.UtcNow + duration;
        var counters = new int[writers];
        var tasks = Enumerable.Range(0, writers).Select(async w =>
        {
            var i = 0;
            while (DateTimeOffset.UtcNow < deadline)
            {
                using var db = CreateContext();
                var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
                var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
                var result = await publish.PublishAsync("SustainedEvent", new PublishEventRequest(
                    AppId: appId, SchemaVersion: 1, Payload: $$"""{ "Marker": "w{{w}}-{{i}}" }""",
                    ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
                Assert.IsInstanceOfType<PublishResult.Accepted>(result, $"writer {w} attempt {i} did not retry away a real conflict within the configured retry budget");
                counters[w] = ++i;
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"Sustained-load publishes per writer: {string.Join(", ", counters)}");
    }
}
