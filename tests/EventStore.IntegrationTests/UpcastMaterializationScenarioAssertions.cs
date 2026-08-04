using EventStore.Domain.EventLog;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Upcast Materialization + Downcast" (docs/08-build-
// plan.md)'s ADR-027 half -- Trigger 1 (publish-time, inline in
// RouterWorker.ProcessEventAsync), Trigger 2 (UpcastMaterializer.
// ReconcileBacklogAsync's background backlog scan), and the fold-skip
// invariant a materialization must never violate (it is a reshaped COPY,
// never re-applied to the Entity Store).
internal static class UpcastMaterializationScenarioAssertions
{
    private static Task RegisterV1(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    private static Task RegisterV2(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["OrderId", "Amount", "Status"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.OrderId as OrderId, event.Amount as Amount, 'Unknown' as Status",
            DowncastToPrevious: "OrderId, Amount"));

    private static async Task<PublishResult.Accepted> Publish(PublishService publish, string appId, string payload)
    {
        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, payload, null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        return (PublishResult.Accepted)result;
    }

    // ADR-027 Trigger 1 -- a lagging publish that's already conformant against
    // its OWN (still-registered) v1 gets its v2 upcast materialized inline,
    // the very first tick that sees v2 as active.
    public static async Task ALaggingConformantPublishGetsItsUpcastMaterializedInlineTheSameTickItsTargetVersionBecomesActive(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "upcast-materialize-1";
        await RegisterV1(registry, appId);
        var original = await Publish(publish, appId, """{ "OrderId": "m-1", "Amount": 42.00 }""");

        await RegisterV2(registry, appId); // v2 now active -- original is still declared/conformant against v1
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var materialization = await db.Events.AsNoTracking()
            .SingleOrDefaultAsync(e => e.MaterializationOfEventId == original.CorrelationId);
        Assert.IsNotNull(materialization, "Trigger 1 should have materialized the lagging publish inline");
        Assert.AreEqual(EventKind.UpcastMaterialization, materialization.EventKind);
        Assert.AreEqual(2, materialization.SchemaVersion);
        Assert.AreEqual("applied", materialization.Status);
        Assert.AreEqual("conformant", materialization.SchemaStatus);
        StringAssert.Contains(materialization.Payload, "\"Unknown\"");
        StringAssert.Contains(materialization.Payload, "42");
    }

    // ADR-027 Trigger 2 -- an event that was already fully "applied" (folded,
    // v1 was the only version that existed at the time) before v2 ever
    // existed has no further "received" processing to hook into; the backlog
    // reconciliation scan is what eventually catches it up.
    public static async Task AnAlreadyAppliedEventFromBeforeAMappingExistedIsMaterializedByTheBacklogReconciliationScan(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "upcast-materialize-2";
        await RegisterV1(registry, appId);
        var original = await Publish(publish, appId, """{ "OrderId": "m-2", "Amount": 17.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // folds and applies against v1 -- no v2 exists yet, Trigger 1 doesn't fire

        var beforeReconcile = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == original.CorrelationId);
        Assert.AreEqual("applied", beforeReconcile.Status, "already fully processed -- won't reappear in a 'received' query");

        await RegisterV2(registry, appId); // now a real backlog exists: one conformant v1 event, active version is v2
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // Trigger 2's ReconcileBacklogAsync runs every tick

        var materialization = await db.Events.AsNoTracking()
            .SingleOrDefaultAsync(e => e.MaterializationOfEventId == original.CorrelationId);
        Assert.IsNotNull(materialization, "Trigger 2's backlog scan should have materialized the pre-existing event");
        Assert.AreEqual(EventKind.UpcastMaterialization, materialization.EventKind);
    }

    // ADR-027's critical invariant -- whichever trigger creates it, a
    // materialization must never be folded into the Entity Store: it is a
    // reshaped COPY of an event that already folded once, under its own
    // EventKind.Original identity. Folding it again would double-apply.
    public static async Task AMaterializedUpcastNeverDoubleAppliesToTheEntityStore(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "upcast-materialize-3";
        await RegisterV1(registry, appId);
        var original = await Publish(publish, appId, """{ "OrderId": "m-3", "Amount": 8.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var rowAfterOriginalFold = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:m-3");
        Assert.AreEqual(1, rowAfterOriginalFold.Version);

        await RegisterV2(registry, appId);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // materializes (Trigger 1 or 2, either way)

        var materialization = await db.Events.AsNoTracking()
            .SingleOrDefaultAsync(e => e.MaterializationOfEventId == original.CorrelationId);
        Assert.IsNotNull(materialization, "precondition: a materialization must actually exist for this to be a meaningful check");

        // Run a further tick too -- ReconcileBacklogAsync's own already-
        // materialized check must also keep skipping it, not just the
        // initial creation.
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var rowAfterMaterialization = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:orderplaced:m-3");
        Assert.AreEqual(1, rowAfterMaterialization.Version, "a materialized upcast must never re-fold and bump Version");

        var materializationCount = await db.Events.CountAsync(e => e.MaterializationOfEventId == original.CorrelationId);
        Assert.AreEqual(1, materializationCount, "must not re-materialize an already-materialized original on a later tick");
    }
}
