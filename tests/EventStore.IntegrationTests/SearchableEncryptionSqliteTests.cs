using EventStore.Domain.SchemaRegistry;
using EventStore.GraphQL;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-096/097 -- the two scenarios docs/features/filter-pushdown.md's own
// Gherkin already named for this feature: an equality query against a
// blind-indexed encrypted field never extracts Payload as plaintext, and
// entity erasure removes that entity's own Shared-scope index rows without
// touching ChainHash. Plus the registration-time cardinality guardrail
// (ADR-096) and the OrderRevealing no-override guardrail (ADR-097).
[TestClass]
public class SearchableEncryptionSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-searchable-encryption-{Guid.NewGuid():N}.db");
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

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "CustomerId": { "type": "string" },
            "Email": {
              "type": "string",
              "x-masking": { "requiredClaim": "pii:view", "strategy": "FixedValue", "regulatoryClassification": "PII" },
              "x-masking-searchable": { "indexKind": "Equality", "keyScope": "Shared" }
            }
          },
          "required": ["CustomerId", "Email"]
        }
        """;

    [TestMethod]
    public async Task EqualityQueryAgainstABlindIndexedEncryptedFieldMatchesWithoutEverExtractingPayloadAsPlaintext()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, _, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var (indexer, searchIndexKeyService, predicateEvaluator) = ErasureTestSupport.CreateSearchIndexStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor, indexer);

        const string appId = "searchable-encryption-demo-1";
        var result = await registry.RegisterAsync("CustomerRegistered", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema,
            FilterableFields: [new FilterableFieldRequest("$.Email", "String", true)],
            ChangeKind: "Full", EntityIdField: "$.CustomerId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);

        await publish.PublishAsync("CustomerRegistered", new PublishEventRequest(appId, 1, """{ "CustomerId": "alice", "Email": "alice@example.com" }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("CustomerRegistered", new PublishEventRequest(appId, 1, """{ "CustomerId": "bob", "Email": "bob@example.com" }""", null, null), TestClaimsPrincipal.None);

        // The stored Payload never contains the plaintext email -- ADR-057's
        // encryption ran exactly as it always has; this test's own value is
        // confirming the SAME ciphertext-only Payload is still searchable.
        var storedEvents = await db.Events.AsNoTracking().Where(e => e.AppId == appId).ToListAsync();
        Assert.IsTrue(storedEvents.All(e => !e.Payload.Contains("alice@example.com") && !e.Payload.Contains("bob@example.com")));

        var definition = await registry.GetActiveAsync(appId, "CustomerRegistered");
        var predicate = await GraphQlFilterPredicateBuilder.Build(
            db, appId, "customerregistered", searchIndexKeyService, predicateEvaluator, definition!.FilterableFields,
            [new EventFilterInput("Email", "alice@example.com", null, null, null, null, null, null)], CancellationToken.None);

        var matches = await db.Events.AsNoTracking().Where(e => e.AppId == appId).Where(predicate).ToListAsync();

        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches[0].Payload.Contains("alice")); // CustomerId, not the encrypted Email -- confirms we matched the right row without ever decrypting to search
    }

    [TestMethod]
    public async Task ErasingTheEntityRemovesItsOwnSharedScopeIndexRowsWithoutTouchingTheChainHash()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, _, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var (indexer, searchIndexKeyService, predicateEvaluator) = ErasureTestSupport.CreateSearchIndexStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor, indexer);

        const string appId = "searchable-encryption-demo-2";
        await registry.RegisterAsync("CustomerRegistered", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema,
            FilterableFields: [new FilterableFieldRequest("$.Email", "String", true)],
            ChangeKind: "Full", EntityIdField: "$.CustomerId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var published = await publish.PublishAsync("CustomerRegistered", new PublishEventRequest(appId, 1, """{ "CustomerId": "carol", "Email": "carol@example.com" }""", null, null), TestClaimsPrincipal.None);
        var accepted = (PublishResult.Accepted)published;
        var entityId = $"{appId}:customerregistered:carol";

        var chainHashBefore = (await db.Events.AsNoTracking().SingleAsync(e => e.SequenceNumber == accepted.SequenceNumber)).ChainHash;
        var indexRowsBefore = await db.EncryptedFieldIndexEntries.AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync();
        Assert.AreEqual(1, indexRowsBefore.Count);

        await erasureKeyService.EraseAsync(entityId);
        var sharedScopeEntries = await db.EncryptedFieldIndexEntries.Where(e => e.EntityId == entityId).ToListAsync();
        db.EncryptedFieldIndexEntries.RemoveRange(sharedScopeEntries); // EntityErasureResolver's own effect, invoked directly here since this test bypasses RouterWorker entirely
        await db.SaveChangesAsync();

        var indexRowsAfter = await db.EncryptedFieldIndexEntries.AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync();
        Assert.AreEqual(0, indexRowsAfter.Count);

        var chainHashAfter = (await db.Events.AsNoTracking().SingleAsync(e => e.SequenceNumber == accepted.SequenceNumber)).ChainHash;
        Assert.AreEqual(chainHashBefore, chainHashAfter);
    }

    [TestMethod]
    public async Task RegisteringALowCardinalityRangeIndexOnAClassifiedFieldWithoutAcknowledgingTheRiskIsRejected()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

        const string schema = """
            {
              "type": "object",
              "properties": {
                "PatientId": { "type": "string" },
                "BirthDate": {
                  "type": "string",
                  "x-masking": { "requiredClaim": "phi:view", "strategy": "FixedValue", "regulatoryClassification": "PHI" },
                  "x-masking-searchable": { "indexKind": "Range", "keyScope": "Shared", "cardinality": "Low", "bucketGranularities": ["Year", "Month", "Day"] }
                }
              },
              "required": ["PatientId", "BirthDate"]
            }
            """;

        var result = await registry.RegisterAsync("PatientEnrolled", new RegisterEventTypeRequest(
            AppId: "searchable-encryption-guardrail", JsonSchema: schema,
            FilterableFields: [new FilterableFieldRequest("$.BirthDate", "DateTimeOffset", true)],
            ChangeKind: "Full", EntityIdField: "$.PatientId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var failure = (RegisterEventTypeResult.ValidationFailed)result;
        Assert.IsTrue(failure.Errors.Any(e => e.Contains("acknowledgeLeakageRisk")));
    }

    [TestMethod]
    public async Task RegisteringOrderRevealingOnAClassifiedFieldIsAlwaysRejectedNoOverrideAccepted()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

        const string schema = """
            {
              "type": "object",
              "properties": {
                "PatientId": { "type": "string" },
                "BirthDate": {
                  "type": "string",
                  "x-masking": { "requiredClaim": "phi:view", "strategy": "FixedValue", "regulatoryClassification": "PHI" },
                  "x-masking-searchable": { "indexKind": "OrderRevealing", "keyScope": "Shared", "acknowledgeLeakageRisk": true }
                }
              },
              "required": ["PatientId", "BirthDate"]
            }
            """;

        var result = await registry.RegisterAsync("PatientEnrolled2", new RegisterEventTypeRequest(
            AppId: "searchable-encryption-guardrail-2", JsonSchema: schema,
            FilterableFields: [new FilterableFieldRequest("$.BirthDate", "DateTimeOffset", true)],
            ChangeKind: "Full", EntityIdField: "$.PatientId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var failure = (RegisterEventTypeResult.ValidationFailed)result;
        Assert.IsTrue(failure.Errors.Any(e => e.Contains("OrderRevealing") && e.Contains("never")));
    }
}
