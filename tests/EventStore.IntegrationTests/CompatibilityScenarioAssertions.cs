using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Compatibility & Deployment Discipline"
// (docs/08-build-plan.md, ADR-038), mirroring docs/08-build-plan.md's own
// named exit criterion (the rollback drill) and docs/features/
// compatibility-and-versioning.md's own Gherkin restatement of it. The
// enum-fallback contract and version-discovery capability negotiation are
// GraphQL-surface behavior, covered separately by
// CompatibilityGraphQlHttpSqliteTests -- this file exercises RouterWorker's
// own forward-incompatibility gate directly, the same "exercise the
// mechanics directly" pattern every other *ScenarioAssertions.cs file here
// already establishes.
internal static class CompatibilityScenarioAssertions
{
    private static Task RegisterOrderPlaced(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task ARolledBackDeploymentDoesNotLoseAnEventTaggedWithASchemaVersionItDoesNotKnow(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "compat-demo-1";
        var upcastChain = UpcastingTestSupport.CreateChain();

        // Active version 1 -- an event tagged SchemaVersion 4 is
        // unambiguously AHEAD of anything this deployment's registry has
        // ever seen, not merely an old/never-registered version (that
        // ordinary backward-compatible case is covered by
        // UpcastingScenarioAssertions/HardeningScenarioAssertions already).
        await RegisterOrderPlaced(registry, appId);

        // PublishService only requires SOME active version to exist
        // (ADR-023) -- it never validates the caller's own declared
        // SchemaVersion against a real registered row, so declaring
        // SchemaVersion 4 directly is a realistic, no-DB-bypass way to
        // simulate "an event tagged with a schema version this
        // deployment's own registry has never seen."
        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "OrderPlaced",
            new PublishEventRequest(appId, 4, """{ "OrderId": "order-rollback-1" }""", null, null),
            TestClaimsPrincipal.None);
        Assert.AreEqual("received", result.Status);

        // "Rolled back" -- the Router ticks while only version 1 is active.
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var stillReceived = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.AreEqual("received", stillReceived.Status, "never lost -- durably persisted (ADR-023), deferred rather than routed against a shape this deployment doesn't know");
        Assert.AreEqual("", stillReceived.EntityId, "not yet routed to any entity");

        // "Re-forward-deploy" -- three more registrations bring the active
        // version up to 4, the same version this event was tagged with.
        for (var i = 0; i < 3; i++)
            await RegisterOrderPlaced(registry, appId);

        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var routedNow = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.AreEqual("applied", routedNow.Status, "becomes routable the moment a later registration covers this version -- no data loss, no database restore");
        Assert.AreEqual("conformant", routedNow.SchemaStatus);
        Assert.AreEqual($"{appId}:orderplaced:order-rollback-1", routedNow.EntityId);
    }

    // The ordinary, already-established "unknown schema" case (an OLD
    // version, never registered, but not AHEAD of active) must be
    // unaffected by the rollback gate above -- ADR-023's "SchemaStatus is
    // advisory, never gates Status" rule still applies here.
    public static async Task AnOldNeverRegisteredSchemaVersionStillReachesAppliedUnaffectedByTheRollbackGate(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "compat-demo-2";
        var upcastChain = UpcastingTestSupport.CreateChain();

        await RegisterOrderPlaced(registry, appId); // version 1
        await RegisterOrderPlaced(registry, appId); // version 2, now active

        // SchemaVersion 1 was registered once but is no longer the active
        // version -- GetVersionAsync still finds row 1 itself, so this
        // doesn't even hit the "declaredDefinition is null" branch at all,
        // let alone the rollback gate.
        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "OrderPlaced",
            new PublishEventRequest(appId, 1, """{ "OrderId": "order-old-1" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.AreEqual("applied", stored.Status);
        Assert.AreEqual("conformant", stored.SchemaStatus);
    }
}
