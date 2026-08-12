using EventStore.Erasure;
using EventStore.Inbox;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-050's STATIC log-redaction shape (distinct from PayloadMasker's own
// dynamic one, already covered by MaskingScenarioAssertions's log-redaction
// tests): PublishServiceLogMessages.PublishRejected's [ActorIdentity]
// parameter should redact the caller's real identity before it ever
// reaches a log sink, using the exact same IRedactorProvider/AddRedaction
// composition root every real Host wires (EventStore.Masking.AddMasking),
// not a hand-rolled substitute.
[TestClass]
public class StaticLogRedactionSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-static-log-redaction-{Guid.NewGuid():N}.db");
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
    public async Task ARejectedPublishLogsWhoWasRejectedWithTheActorIdentityRedacted()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(logs));
        services.AddErasure(new ConfigurationBuilder().Build());
        services.AddMasking(new Dictionary<string, string>()); // no HMAC keys needed -- ActorIdentityTaxonomy has no explicit redactor registered, falls back to ErasingRedactor (AddMasking's own documented default)
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<PublishService>>();

        const string appId = "static-log-redaction-demo-1";
        await registry.RegisterAsync("RestrictedThingHappened", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null,
            RequiredClaims: [new RequiredClaimRequest("Publish", "scope:restricted:create")],
            UpcastFromPrevious: null, DowncastToPrevious: null));

        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), logger: logger);

        const string secretActorId = "actor-secret-identity-99";
        var caller = TestClaimsPrincipal.WithClaims(("sub", secretActorId));
        var result = await publish.PublishAsync("RestrictedThingHappened",
            new PublishEventRequest(appId, 1, """{ "Id": "thing-1" }""", null, null), caller);

        Assert.IsInstanceOfType<PublishResult.Forbidden>(result);
        Assert.IsTrue(logs.Messages.Count > 0, "expected the rejection to actually log something");
        var combined = string.Join('\n', logs.Messages);
        StringAssert.Contains(combined, "restrictedthinghappened");
        StringAssert.Contains(combined, "missing required claim");
        Assert.IsFalse(combined.Contains(secretActorId), "the real actor identity must never reach the log sink unredacted");
    }
}
