using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Lineage API (read side)" (docs/08-build-plan.md),
// mirroring the publish-with-parents + querying scenarios in
// docs/features/event-chains.md that this build stage's own plain
// QUERY /events/{id}/parents|children|ancestors|descendants surface covers
// (the GraphQL event(eventId){...} shape belongs to "GraphQL-Only Query
// Layer", much later).
internal static class LineageScenarioAssertions
{
    private const string SimpleSchema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";

    private static async Task RegisterSimpleType(SchemaRegistryService registry, string appId, string typeName, string parentValidationMode = "Strict") =>
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: parentValidationMode, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task PublishingAnOriginEventShowsNoParents(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-1";
        await RegisterSimpleType(registry, appId, "OrderPlaced");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            appId, 1, """{ "Amount": 1 }""", null, null));
        var eventId = ((PublishResult.Created)result).EventId;

        var parents = await lineage.GetParentsAsync(eventId, null, null);
        Assert.IsEmpty(parents);
    }

    public static async Task FetchingImmediateParentsAndChildrenReturnsExactlyThoseRelationships(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-2";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var parentResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null));
        var parentId = ((PublishResult.Created)parentResult).EventId;

        var childResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [parentId], null));
        var childId = ((PublishResult.Created)childResult).EventId;

        var children = await lineage.GetChildrenAsync(parentId, null, null);
        Assert.HasCount(1, children);
        Assert.AreEqual(childId, children[0].EventId);
        Assert.IsTrue(children[0].Resolved);

        var parents = await lineage.GetParentsAsync(childId, null, null);
        Assert.HasCount(1, parents);
        Assert.AreEqual(parentId, parents[0].EventId);
    }

    public static async Task PermissiveValidationAcceptsADanglingParentReferenceShowingResolvedFalse(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-3";
        await RegisterSimpleType(registry, appId, "OrderShipped", parentValidationMode: "Permissive");
        var danglingParentId = Guid.NewGuid();

        var result = await publish.PublishAsync("OrderShipped", new PublishEventRequest(
            appId, 1, """{ "Amount": 1 }""", [danglingParentId], null));
        var eventId = ((PublishResult.Created)result).EventId;

        var parents = await lineage.GetParentsAsync(eventId, null, null);
        Assert.HasCount(1, parents);
        Assert.AreEqual(danglingParentId, parents[0].EventId);
        Assert.IsFalse(parents[0].Resolved);
    }

    public static async Task AncestorTraversalTerminatesAcrossAPermissiveCycle(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-4";
        await RegisterSimpleType(registry, appId, "OrderPlaced", parentValidationMode: "Permissive");
        await RegisterSimpleType(registry, appId, "PaymentReceived", parentValidationMode: "Permissive");

        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // order-1 published first, dangling parentEventId payment-1 (doesn't exist yet)
        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [paymentId], orderId));
        // payment-1 published, parented off order-1 (which now exists) -- closes the 2-cycle
        await publish.PublishAsync("PaymentReceived", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], paymentId));

        var ancestors = await lineage.GetAncestorsAsync(orderId, null, null);
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == paymentId));

        var descendants = await lineage.GetDescendantsAsync(orderId, null, null);
        Assert.AreEqual(1, descendants.Count(d => d.EventId == paymentId));
    }

    public static async Task MultiHopAncestorChainReturnsEveryAncestor(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-5";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "PaymentReceived");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null));
        var orderId = ((PublishResult.Created)orderResult).EventId;

        var paymentResult = await publish.PublishAsync("PaymentReceived", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null));
        var paymentId = ((PublishResult.Created)paymentResult).EventId;

        var shipResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [paymentId], null));
        var shipId = ((PublishResult.Created)shipResult).EventId;

        var ancestors = await lineage.GetAncestorsAsync(shipId, null, null);
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == paymentId));
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == orderId));
    }

    public static async Task FetchingLineageForAnUnknownEventIsRejected(LineageService lineage)
    {
        Assert.IsFalse(await lineage.EventExistsAsync(Guid.NewGuid()));
    }

    public static async Task TopAndSkipCorrectlySliceAResultAndOmittingBothReturnsEverything(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-6";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null));
        var orderId = ((PublishResult.Created)orderResult).EventId;

        for (var i = 0; i < 5; i++)
            await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null));

        var page = await lineage.GetChildrenAsync(orderId, top: 2, skip: 0);
        Assert.HasCount(2, page);

        var all = await lineage.GetChildrenAsync(orderId, top: null, skip: null);
        Assert.HasCount(5, all);
    }
}
