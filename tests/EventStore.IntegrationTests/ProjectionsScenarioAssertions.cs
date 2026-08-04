using System.Net;
using System.Net.Http.Json;
using EventStore.Projections.Abstractions;
using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Orders.Projections;

namespace EventStore.IntegrationTests;

// Shared scenarios for "CQRS Read-Model Projections (worked example)"
// (docs/08-build-plan.md, ADR-015/016), covering docs/features/cqrs-
// projections.md's Gherkin scenarios translated to this item's own,
// explicitly pre-ADR-022 scope (whole-payload Partial merge -- no
// Optional<T> wrapper, no explicit-null-clears-a-field; both later
// revisions). Unlike every other item's tests, this one drives real HTTP
// end to end -- registering/publishing through the real Host TestServer with
// real tokens (ProjectionHost's own only reachable dependency on the write
// side is HTTP, docs/06-solution-structure.md) -- and drives ProjectionHost
// itself via its bounded CatchUpOnceAsync rather than an unboundedly live
// background loop, the same "exercise the mechanics directly, with a
// timeout" pattern this repo's own Follow-consuming tests already use for
// Follow's inherently-infinite SSE stream.
internal static class ProjectionsScenarioAssertions
{
    private static readonly TimeSpan CatchUpIdleTimeout = TimeSpan.FromMilliseconds(500);

    public static async Task RegisterEventTypeAsync(
        HttpClient hostClient, HttpClient devIdpClient, string appId, string typeName, string changeKind, string schema, string entityIdField,
        (string Direction, string Claim)[]? requiredClaims = null)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{typeName}")
        {
            Content = JsonContent.Create(new
            {
                appId,
                jsonSchema = schema,
                filterableFields = Array.Empty<object>(),
                changeKind,
                entityIdField,
                parentValidationMode = "Permissive",
                requiredClaims = requiredClaims?.Select(c => new { direction = c.Direction, claim = c.Claim }).ToArray(),
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, hostClient, token, key);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task PublishAsync(HttpClient hostClient, HttpClient devIdpClient, string appId, string typeName, string payload)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{typeName}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload }),
        };
        AuthScenarioAssertions.AttachAuth(request, hostClient, token, key);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task RunCatchUpForAllEventTypesAsync(ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, CancellationToken ct = default)
    {
        foreach (var eventType in projection.EventTypes)
            await host.CatchUpOnceAsync(eventType, int.MaxValue, CatchUpIdleTimeout, ct);
    }

    private static readonly string OrderPlacedSchema = """
        { "type": "object", "properties": {
            "OrderId": { "type": "string" }, "CustomerName": { "type": "string" },
            "Address": { "type": "string" }, "Amount": { "type": "number" }
          }, "required": ["OrderId", "CustomerName", "Address", "Amount"] }
        """;
    private static readonly string OrderAddressUpdatedSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "Address": { "type": "string" } }, "required": ["OrderId"] }
        """;
    private static readonly string OrderShippedSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "ShippedAt": { "type": "string" } }, "required": ["OrderId"] }
        """;
    private static readonly string OrderCancelledSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "CancelledAt": { "type": "string" } }, "required": ["OrderId"] }
        """;

    private static async Task RegisterTheFourOrderEventTypesAsync(HttpClient hostClient, HttpClient devIdpClient, string appId)
    {
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderPlaced", "Full", OrderPlacedSchema, "$.OrderId");
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderAddressUpdated", "Partial", OrderAddressUpdatedSchema, "$.OrderId");
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderShipped", "Partial", OrderShippedSchema, "$.OrderId");
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderCancelled", "Partial", OrderCancelledSchema, "$.OrderId");
    }

    public static async Task AFullEventEstablishesTheReadModelRowFromScratch(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterTheFourOrderEventTypesAsync(hostClient, devIdpClient, appId);
        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-1", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var db = createDb();
        var row = await db.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-1");
        Assert.AreEqual("A. Smith", row.CustomerName);
        Assert.AreEqual("10 Downing St", row.Address);
        Assert.AreEqual(42.00m, row.Amount);
        Assert.IsNull(row.ShippedAt);
        Assert.IsNull(row.CancelledAt);
    }

    public static async Task APartialEventMergesOntoExistingStateLeavingUntouchedFieldsAlone(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterTheFourOrderEventTypesAsync(hostClient, devIdpClient, appId);
        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-2", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderAddressUpdated", """{ "OrderId": "o-2", "Address": "221B Baker St" }""");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var db = createDb();
        var row = await db.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-2");
        Assert.AreEqual("221B Baker St", row.Address);
        Assert.AreEqual("A. Smith", row.CustomerName);
        Assert.AreEqual(42.00m, row.Amount);
    }

    public static async Task IndependentPartialEventsEachMergeWithoutClobberingTheOthersFields(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterTheFourOrderEventTypesAsync(hostClient, devIdpClient, appId);
        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-3", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderShipped", """{ "OrderId": "o-3", "ShippedAt": "2026-01-05T10:00:00Z" }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderCancelled", """{ "OrderId": "o-3", "CancelledAt": "2026-01-06T10:00:00Z" }""");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var db = createDb();
        var row = await db.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-3");
        Assert.AreEqual(DateTimeOffset.Parse("2026-01-05T10:00:00Z"), row.ShippedAt);
        Assert.AreEqual(DateTimeOffset.Parse("2026-01-06T10:00:00Z"), row.CancelledAt);
        Assert.AreEqual("10 Downing St", row.Address);
    }

    public static async Task AMaskedOrAbsentFieldInAPartialPayloadIsIgnoredOnMergeNeverOverlaidAsAPlaceholder(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderPlaced", "Full", OrderPlacedSchema, "$.OrderId");
        // OrderAddressUpdated gated by a Read-direction claim projections-client's
        // token (scope events:follow only, no claims) doesn't hold.
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderAddressUpdated", "Partial", OrderAddressUpdatedSchema, "$.OrderId",
            requiredClaims: [("Read", "clearance:secret")]);
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderShipped", "Partial", OrderShippedSchema, "$.OrderId");
        await RegisterEventTypeAsync(hostClient, devIdpClient, appId, "OrderCancelled", "Partial", OrderCancelledSchema, "$.OrderId");

        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-4", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderAddressUpdated", """{ "OrderId": "o-4", "Address": "221B Baker St" }""");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var db = createDb();
        var row = await db.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-4");
        Assert.AreEqual("10 Downing St", row.Address, "the projection cannot see the restricted OrderAddressUpdated event at all (403 at connect time)");
    }

    public static async Task RegisteringAnEventTypeWithoutChangeKindIsRejected(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/OrderRefunded")
        {
            Content = JsonContent.Create(new
            {
                appId = "orders-demo",
                jsonSchema = """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
                filterableFields = Array.Empty<object>(),
                entityIdField = "$.OrderId",
                parentValidationMode = "Permissive",
            }),
        };
        AuthScenarioAssertions.AttachAuth(request, hostClient, token, key);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static async Task FullRebuildFromScratchReproducesTheSameEndStateAsIncrementalApplication(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterTheFourOrderEventTypesAsync(hostClient, devIdpClient, appId);
        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-6", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderAddressUpdated", """{ "OrderId": "o-6", "Address": "221B Baker St" }""");
        await PublishAsync(hostClient, devIdpClient, appId, "OrderShipped", """{ "OrderId": "o-6", "ShippedAt": "2026-01-05T10:00:00Z" }""");

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var beforeDb = createDb();
        var before = await beforeDb.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-6");
        var beforeAddress = before.Address;
        var beforeShippedAt = before.ShippedAt;

        // Truncate the read-model table + snapshots, reset the checkpoint to 0 --
        // docs/09-cqrs-read-models.md's own "same code path as incremental
        // catch-up, starting from 0" rebuild.
        using (var rebuildDb = createDb())
        {
            rebuildDb.OrderSummaries.RemoveRange(rebuildDb.OrderSummaries);
            rebuildDb.Snapshots.RemoveRange(rebuildDb.Snapshots.Where(s => s.ProjectionName == projection.Name));
            rebuildDb.Checkpoints.RemoveRange(rebuildDb.Checkpoints.Where(c => c.ProjectionName == projection.Name));
            await rebuildDb.SaveChangesAsync();
        }

        await RunCatchUpForAllEventTypesAsync(host, projection);

        using var afterDb = createDb();
        var after = await afterDb.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-6");
        Assert.AreEqual(beforeAddress, after.Address);
        Assert.AreEqual(beforeShippedAt, after.ShippedAt);
    }

    public static async Task IncrementalResumeAfterDowntimeDeliversNoGapAndNoDuplicate(
        HttpClient hostClient, HttpClient devIdpClient, ProjectionHost<OrderSummary> host, IProjection<OrderSummary> projection, Func<OrdersProjectionsDbContext> createDb)
    {
        const string appId = "orders-demo";
        await RegisterTheFourOrderEventTypesAsync(hostClient, devIdpClient, appId);
        await PublishAsync(hostClient, devIdpClient, appId, "OrderPlaced", """{ "OrderId": "o-7", "CustomerName": "A. Smith", "Address": "10 Downing St", "Amount": 42.00 }""");

        await RunCatchUpForAllEventTypesAsync(host, projection); // "the projection is stopped" after processing OrderPlaced

        await PublishAsync(hostClient, devIdpClient, appId, "OrderShipped", """{ "OrderId": "o-7", "ShippedAt": "2026-01-05T10:00:00Z" }"""); // published while "stopped"

        var secondRunConsumedCount = 0;
        foreach (var eventType in projection.EventTypes)
            secondRunConsumedCount += await host.CatchUpOnceAsync(eventType, int.MaxValue, CatchUpIdleTimeout, CancellationToken.None);

        // Exactly one new event (OrderShipped) is delivered on resume -- OrderPlaced,
        // already reflected via the checkpoint, is not redelivered.
        Assert.AreEqual(1, secondRunConsumedCount);

        using var db = createDb();
        var row = await db.OrderSummaries.AsNoTracking().SingleAsync(o => o.OrderId == "o-7");
        Assert.AreEqual(DateTimeOffset.Parse("2026-01-05T10:00:00Z"), row.ShippedAt);
        Assert.AreEqual("A. Smith", row.CustomerName);
    }
}
