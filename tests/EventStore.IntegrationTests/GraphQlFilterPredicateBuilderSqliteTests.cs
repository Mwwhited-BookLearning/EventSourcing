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

// "GraphQL-Only Query Layer" -- GraphQlFilterPredicateBuilder is the ONE
// genuinely new piece of logic in this item's filter translation (multi-
// clause AND-combination, per-operator dispatch); the per-provider native
// SQL generation underneath (JsonFunctions/IJsonPathTranslator) is REUSED
// UNCHANGED from "Follow API + Filter Pushdown," already proven across
// SQLite/PostgreSQL/SQL Server by that item's own FollowSqliteTests/
// Postgres/SqlServer test classes -- exercising it a fourth time per
// provider here would prove nothing new, so this is SQLite-only,
// deliberately, not a shortcut on anything unproven.
[TestClass]
public class GraphQlFilterPredicateBuilderSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-graphql-filter-{Guid.NewGuid():N}.db");
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
    public async Task AGreaterThanClauseOnANumberFieldPushesDownToNativeSql()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        const string appId = "graphql-filter-demo-1";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }""",
            FilterableFields: [new FilterableFieldRequest("$.Amount", "Number", false)],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "f-1", "Amount": 30 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "f-2", "Amount": 75 }""", null, null), TestClaimsPrincipal.None);

        var definition = await registry.GetActiveAsync(appId, "OrderPlaced");
        var predicate = GraphQlFilterPredicateBuilder.Build(definition!.FilterableFields, [new EventFilterInput("Amount", null, null, "50", null, null, null, null)]);

        var matches = await db.Events.AsNoTracking()
            .Where(e => e.AppId == appId && e.EventType == "orderplaced")
            .Where(predicate)
            .ToListAsync();

        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches[0].Payload.Contains("f-2"));
    }

    [TestMethod]
    public async Task MultipleClausesCombineWithAnd()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());

        const string appId = "graphql-filter-demo-2";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["OrderId", "Amount", "Status"] }""",
            FilterableFields: [new FilterableFieldRequest("$.Amount", "Number", false), new FilterableFieldRequest("$.Status", "String", false)],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "f-3", "Amount": 75, "Status": "Shipped" }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "f-4", "Amount": 75, "Status": "Pending" }""", null, null), TestClaimsPrincipal.None);

        var definition = await registry.GetActiveAsync(appId, "OrderPlaced");
        var predicate = GraphQlFilterPredicateBuilder.Build(definition!.FilterableFields,
        [
            new EventFilterInput("Amount", null, null, "50", null, null, null, null),
            new EventFilterInput("Status", "Shipped", null, null, null, null, null, null),
        ]);

        var matches = await db.Events.AsNoTracking()
            .Where(e => e.AppId == appId && e.EventType == "orderplaced")
            .Where(predicate)
            .ToListAsync();

        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches[0].Payload.Contains("f-3"));
    }

    [TestMethod]
    public async Task AnUndeclaredFieldNameThrowsBeforeTouchingTheDatabase()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());

        const string appId = "graphql-filter-demo-3";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var definition = await registry.GetActiveAsync(appId, "OrderPlaced");
        Assert.ThrowsExactly<HotChocolate.GraphQLException>(() =>
            GraphQlFilterPredicateBuilder.Build(definition!.FilterableFields, [new EventFilterInput("NotDeclared", "x", null, null, null, null, null, null)]));
    }
}
