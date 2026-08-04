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

    private static async Task RegisterReadClaimGatedType(SchemaRegistryService registry, string appId, string typeName) =>
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Strict",
            RequiredClaims: [new RequiredClaimRequest("Read", "clearance:phi")],
            UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task PublishingAnOriginEventShowsNoParents(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-1";
        await RegisterSimpleType(registry, appId, "OrderPlaced");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var eventId = ((PublishResult.Accepted)result).CorrelationId;

        var parents = await lineage.GetParentsAsync(eventId, TestClaimsPrincipal.None, null, null);
        Assert.IsEmpty(parents);
    }

    public static async Task FetchingImmediateParentsAndChildrenReturnsExactlyThoseRelationships(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-2";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var parentResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var parentId = ((PublishResult.Accepted)parentResult).CorrelationId;

        var childResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [parentId], null), TestClaimsPrincipal.None);
        var childId = ((PublishResult.Accepted)childResult).CorrelationId;

        var children = await lineage.GetChildrenAsync(parentId, TestClaimsPrincipal.None, null, null);
        Assert.HasCount(1, children);
        Assert.AreEqual(childId, children[0].EventId);
        Assert.IsTrue(children[0].Resolved);

        var parents = await lineage.GetParentsAsync(childId, TestClaimsPrincipal.None, null, null);
        Assert.HasCount(1, parents);
        Assert.AreEqual(parentId, parents[0].EventId);
    }

    public static async Task PermissiveValidationAcceptsADanglingParentReferenceShowingResolvedFalse(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-3";
        await RegisterSimpleType(registry, appId, "OrderShipped", parentValidationMode: "Permissive");
        var danglingParentId = Guid.NewGuid();

        var result = await publish.PublishAsync("OrderShipped", new PublishEventRequest(
            appId, 1, """{ "Amount": 1 }""", [danglingParentId], null), TestClaimsPrincipal.None);
        var eventId = ((PublishResult.Accepted)result).CorrelationId;

        var parents = await lineage.GetParentsAsync(eventId, TestClaimsPrincipal.None, null, null);
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
        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [paymentId], orderId), TestClaimsPrincipal.None);
        // payment-1 published, parented off order-1 (which now exists) -- closes the 2-cycle
        await publish.PublishAsync("PaymentReceived", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], paymentId), TestClaimsPrincipal.None);

        var ancestors = await lineage.GetAncestorsAsync(orderId, TestClaimsPrincipal.None, null, null);
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == paymentId));

        var descendants = await lineage.GetDescendantsAsync(orderId, TestClaimsPrincipal.None, null, null);
        Assert.AreEqual(1, descendants.Count(d => d.EventId == paymentId));
    }

    public static async Task MultiHopAncestorChainReturnsEveryAncestor(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-5";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "PaymentReceived");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var orderId = ((PublishResult.Accepted)orderResult).CorrelationId;

        var paymentResult = await publish.PublishAsync("PaymentReceived", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null), TestClaimsPrincipal.None);
        var paymentId = ((PublishResult.Accepted)paymentResult).CorrelationId;

        var shipResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [paymentId], null), TestClaimsPrincipal.None);
        var shipId = ((PublishResult.Accepted)shipResult).CorrelationId;

        var ancestors = await lineage.GetAncestorsAsync(shipId, TestClaimsPrincipal.None, null, null);
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == paymentId));
        Assert.AreEqual(1, ancestors.Count(a => a.EventId == orderId));
    }

    public static async Task FetchingLineageForAnUnknownEventIsRejected(LineageService lineage)
    {
        Assert.AreEqual(LineageRootCheck.NotFound, await lineage.CheckRootAsync(Guid.NewGuid(), TestClaimsPrincipal.None));
    }

    public static async Task TopAndSkipCorrectlySliceAResultAndOmittingBothReturnsEverything(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-6";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var orderId = ((PublishResult.Accepted)orderResult).CorrelationId;

        for (var i = 0; i < 5; i++)
            await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null), TestClaimsPrincipal.None);

        var page = await lineage.GetChildrenAsync(orderId, TestClaimsPrincipal.None, top: 2, skip: 0);
        Assert.HasCount(2, page);

        var all = await lineage.GetChildrenAsync(orderId, TestClaimsPrincipal.None, top: null, skip: null);
        Assert.HasCount(5, all);
    }

    public static async Task ARestrictedRootIsRejectedWith403DistinctFromAnUnknownRootsNotFound(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-7";
        const string typeName = "PatientAdmitted";
        await RegisterReadClaimGatedType(registry, appId, typeName);

        var result = await publish.PublishAsync(typeName, new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var eventId = ((PublishResult.Accepted)result).CorrelationId;

        Assert.AreEqual(LineageRootCheck.Forbidden, await lineage.CheckRootAsync(eventId, TestClaimsPrincipal.None));
        Assert.AreEqual(LineageRootCheck.Ok, await lineage.CheckRootAsync(eventId, TestClaimsPrincipal.With("clearance:phi")));
        Assert.AreEqual(LineageRootCheck.NotFound, await lineage.CheckRootAsync(Guid.NewGuid(), TestClaimsPrincipal.None));
    }

    // ADR-008: "traversal does not recurse past a node the caller can't see."
    // Chain: OrderPlaced (visible) <- RestrictedPayment (restricted) <- OrderShipped
    // (visible, the query root). From the root's ancestors, RestrictedPayment must
    // appear as a stub, but OrderPlaced -- itself unrestricted -- must NOT appear
    // at all, since the only path to it runs through the restricted node.
    public static async Task AncestorTraversalStopsAtARestrictedNodeInsteadOfJustRedactingItsFields(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-8";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterReadClaimGatedType(registry, appId, "RestrictedPayment");
        await RegisterSimpleType(registry, appId, "OrderShipped");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var orderId = ((PublishResult.Accepted)orderResult).CorrelationId;

        var paymentResult = await publish.PublishAsync("RestrictedPayment", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null), TestClaimsPrincipal.None);
        var paymentId = ((PublishResult.Accepted)paymentResult).CorrelationId;

        var shipResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [paymentId], null), TestClaimsPrincipal.None);
        var shipId = ((PublishResult.Accepted)shipResult).CorrelationId;

        var ancestors = await lineage.GetAncestorsAsync(shipId, TestClaimsPrincipal.None, null, null);

        var paymentNode = ancestors.Single(a => a.EventId == paymentId);
        Assert.IsTrue(paymentNode.Resolved);
        Assert.IsTrue(paymentNode.Restricted);
        Assert.IsNull(paymentNode.EventType);
        Assert.IsFalse(ancestors.Any(a => a.EventId == orderId), "traversal must not recurse past the restricted node to reach its own visible ancestor");

        // With the claim, the restricted node opens up and its own ancestor becomes reachable again.
        var ancestorsWithClaim = await lineage.GetAncestorsAsync(shipId, TestClaimsPrincipal.With("clearance:phi"), null, null);
        Assert.IsTrue(ancestorsWithClaim.Any(a => a.EventId == paymentId && !a.Restricted));
        Assert.IsTrue(ancestorsWithClaim.Any(a => a.EventId == orderId));
    }

    // Two children of the same parent: one restricted, one not. Fetching the
    // parent's children must stub the restricted one without omitting or
    // otherwise affecting the sibling -- the two are evaluated independently.
    public static async Task ARestrictedSiblingNeverAffectsAnOtherwiseVisibleSibling(SchemaRegistryService registry, PublishService publish, LineageService lineage)
    {
        const string appId = "lineage-demo-9";
        await RegisterSimpleType(registry, appId, "OrderPlaced");
        await RegisterSimpleType(registry, appId, "OrderShipped");
        await RegisterReadClaimGatedType(registry, appId, "RestrictedPayment");

        var orderResult = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var orderId = ((PublishResult.Accepted)orderResult).CorrelationId;

        var shipResult = await publish.PublishAsync("OrderShipped", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null), TestClaimsPrincipal.None);
        var shipId = ((PublishResult.Accepted)shipResult).CorrelationId;

        var paymentResult = await publish.PublishAsync("RestrictedPayment", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", [orderId], null), TestClaimsPrincipal.None);
        var paymentId = ((PublishResult.Accepted)paymentResult).CorrelationId;

        var children = await lineage.GetChildrenAsync(orderId, TestClaimsPrincipal.None, null, null);
        Assert.HasCount(2, children);

        var shipNode = children.Single(c => c.EventId == shipId);
        Assert.IsTrue(shipNode.Resolved);
        Assert.IsFalse(shipNode.Restricted);
        Assert.AreEqual("ordershipped", shipNode.EventType);

        var paymentNode = children.Single(c => c.EventId == paymentId);
        Assert.IsTrue(paymentNode.Resolved);
        Assert.IsTrue(paymentNode.Restricted);
    }
}
