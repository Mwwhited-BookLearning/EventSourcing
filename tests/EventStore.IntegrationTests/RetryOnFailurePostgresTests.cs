using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(_container.GetConnectionString(), x => x
                .MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")
                .EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: ["3D000", "40001"]))
            .Options;
        return new EventStoreContext(options, new PostgresJsonPathTranslator());
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
}
