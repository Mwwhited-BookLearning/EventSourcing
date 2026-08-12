using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Entity-Centric Core Rebuild" (docs/08-build-plan.md),
// mirroring docs/features/entity-concept.md's Gherkin one-for-one. Unlike
// PublishScenarioAssertions (which deliberately never touches the Router,
// since "Publish API" is scoped to what's synchronously observable), every
// scenario here drives RouterWorker.RunOnceAsync directly after publishing --
// the same "exercise the mechanics directly" pattern DerivationWorker's own
// tests already established, not a live background loop.
internal static class EntityScenarioAssertions
{
    // Both event types declare EntityType: "Order" explicitly -- OrderPlaced
    // and OrderShipped are different event TYPES patching the same logical
    // entity, so they must resolve to the same EntityId despite their
    // default-to-own-name EntityType fallbacks differing ("orderplaced" vs
    // "ordershipped") otherwise.
    private static Task RegisterOrderPlaced(SchemaRegistryService registry, string appId, string changeKind = "Full") =>
        registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "CustomerName": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }
                """,
            FilterableFields: [], ChangeKind: changeKind, EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Order"));

    private static Task RegisterOrderShipped(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderShipped", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "Carrier": { "type": "string" } }, "required": ["OrderId", "Carrier"] }
                """,
            FilterableFields: [], ChangeKind: "Partial", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Order"));

    // A second Partial patch type touching a DIFFERENT property than
    // OrderShipped's own Carrier -- ADR-024's own named "not a conflict"
    // case needs two patches on genuinely different properties to exercise
    // at all; OrderShipped alone only ever touches Carrier.
    private static Task RegisterOrderCustomerNameUpdated(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("OrderCustomerNameUpdated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """
                { "type": "object", "properties": { "OrderId": { "type": "string" }, "CustomerName": { "type": "string" } }, "required": ["OrderId", "CustomerName"] }
                """,
            FilterableFields: [], ChangeKind: "Partial", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Order"));

    private static async Task<PublishResult.Accepted> Publish(
        PublishService publish, string appId, string typeName, string payload, long? expectedVersion = null)
    {
        var result = await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, payload, null, null, expectedVersion), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        return (PublishResult.Accepted)result;
    }

    // OccurredAt would ordinarily be stamped from the client's own clock at
    // publish time; the LateArrivalFlag scenarios below need to control it
    // directly and deterministically instead of racing real wall-clock
    // timestamps, so they backdate it via a direct db edit after publishing,
    // before the Router ever sees the row.
    private static async Task SetOccurredAtAsync(EventStoreContext db, Guid eventId, DateTimeOffset occurredAt)
    {
        var row = await db.Events.SingleAsync(e => e.EventId == eventId);
        row.OccurredAt = occurredAt;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static async Task PublishingAnEventThatResolvesToABrandNewEntityIdCreatesAnEntityStoreRow(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-1";
        await RegisterOrderPlaced(registry, appId);

        var created = await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-1", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        Assert.AreEqual("received", created.Status);

        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == created.CorrelationId);
        Assert.AreEqual("applied", storedEvent.Status);
        Assert.AreEqual($"{appId}:order:o-1", storedEvent.EntityId);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-1");
        Assert.AreEqual(1, row.Version);
    }

    public static async Task PublishingASecondEventForTheSameEntityIdUpdatesTheRowAndIncrementsVersion(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-2";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-2", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-2", "Carrier": "UPS" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-2");
        Assert.AreEqual(2, row.Version);
        StringAssert.Contains(row.Data, "A. Smith");
        StringAssert.Contains(row.Data, "42");
    }

    public static async Task AFullEventsPayloadReplacesTheEntityStoreRowsDataWholesale(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-3";
        await RegisterOrderPlaced(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-3", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-3", "CustomerName": "A. Smith", "Amount": 99.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-3");
        StringAssert.Contains(row.Data, "99");
        Assert.IsFalse(row.Data.Contains("42"), "a Full payload replaces Data wholesale, not merges");
    }

    public static async Task APartialEventsUnknownPropertyIsFoldedIntoExtensionsBagNotDropped(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-4";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-4", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-4", "Carrier": "UPS", "TrackingNumber": "1Z999" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-4");
        StringAssert.Contains(row.Data, "UPS");
        StringAssert.Contains(row.Extensions, "1Z999");
    }

    public static async Task PublishingWithAStaleExpectedVersionSetsConflictFlagButStillPersistsAndFolds(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-5";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-5", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);
        await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-5", "Carrier": "UPS" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // EntityStoreRow now at Version 2

        var stale = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-5", "Carrier": "FedEx" }""", expectedVersion: 1);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == stale.CorrelationId);
        Assert.IsTrue(storedEvent.ConflictFlag);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-5");
        Assert.AreEqual(3, row.Version, "ExpectedVersion never blocks a fold -- it only flags the later event as conflicting");
        StringAssert.Contains(row.Data, "FedEx");
    }

    public static async Task AnEventWithAnOlderOccurredAtArrivingAfterALogicallyNewerOneAlreadyFoldedSetsLateArrivalFlagAndDoesNotOverwrite(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-6";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        // Relative to a captured baseline, not fixed calendar dates -- OrderPlaced's
        // own OccurredAt is real DateTimeOffset.UtcNow at publish time, so a
        // fixed-in-the-past literal risks landing BEHIND that (making OrderPlaced
        // itself the "late" one relative to nothing) depending on when the suite runs.
        var baseline = DateTimeOffset.UtcNow;
        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-6", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var eB = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-6", "Carrier": "UPS" }""");
        await SetOccurredAtAsync(db, eB.CorrelationId, baseline.AddHours(2));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var eA = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-6", "Carrier": "FedEx" }""");
        await SetOccurredAtAsync(db, eA.CorrelationId, baseline.AddHours(1)); // earlier than eB, arrives after
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEventA = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eA.CorrelationId);
        Assert.IsTrue(storedEventA.LateArrivalFlag);
        Assert.IsFalse(storedEventA.ConflictFlag);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-6");
        StringAssert.Contains(row.Data, "UPS");
        Assert.IsFalse(row.Data.Contains("FedEx"), "a late arrival's change must never overwrite already-folded newer state");
    }

    public static async Task AnEventThatIsBothAStaleExpectedVersionConflictAndALateArrivalSetsBothFlagsIndependently(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-7";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        var baseline = DateTimeOffset.UtcNow;
        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-7", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var shipped = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-7", "Carrier": "UPS" }""");
        await SetOccurredAtAsync(db, shipped.CorrelationId, baseline.AddHours(2));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // EntityStoreRow now at Version 2, LastAppliedLogicalTime baseline+2h

        var both = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-7", "Carrier": "FedEx" }""", expectedVersion: 1);
        await SetOccurredAtAsync(db, both.CorrelationId, baseline.AddHours(1));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == both.CorrelationId);
        Assert.IsTrue(storedEvent.ConflictFlag);
        Assert.IsTrue(storedEvent.LateArrivalFlag);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-7");
        Assert.AreEqual(2, row.Version, "LateArrivalFlag gates the write, so Version never advances to 3 here");
        StringAssert.Contains(row.Data, "UPS");
    }

    // ADR-024's own Decision, verbatim: "two patches based on the same
    // version touching DIFFERENT properties both fold cleanly regardless
    // of arrival order -- that is not a conflict." Before this item, the
    // fold compared whole-entity ExpectedVersion to row.Version alone, so
    // OrderCustomerNameUpdated below (anchored at the version right after
    // OrderPlaced, same as OrderShipped) would have been WRONGLY flagged
    // once OrderShipped's own fold had already bumped row.Version past 1
    // -- a real regression this test would have caught.
    public static async Task TwoPatchesBasedOnTheSameVersionTouchingDifferentPropertiesBothFoldCleanlyWithNoConflict(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-10";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);
        await RegisterOrderCustomerNameUpdated(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-10", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // EntityStoreRow now at Version 1

        // Both anchor ExpectedVersion at 1 (right after OrderPlaced), but
        // touch DIFFERENT properties -- Carrier vs CustomerName.
        var shipped = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-10", "Carrier": "UPS" }""", expectedVersion: 1);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // Version 2 -- only Carrier's own PropertyVersions entry advances

        var renamed = await Publish(publish, appId, "OrderCustomerNameUpdated", """{ "OrderId": "o-10", "CustomerName": "B. Jones" }""", expectedVersion: 1);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // Version 3

        var shippedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == shipped.CorrelationId);
        var renamedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == renamed.CorrelationId);
        Assert.IsFalse(shippedEvent.ConflictFlag, "touches Carrier only");
        Assert.IsFalse(renamedEvent.ConflictFlag, "touches CustomerName only, based on the same version OrderShipped was -- not a conflict per ADR-024");

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-10");
        Assert.AreEqual(3, row.Version);
        StringAssert.Contains(row.Data, "UPS");
        StringAssert.Contains(row.Data, "B. Jones");
    }

    // ADR-029's late-arrival guard, made per-property alongside ADR-024's
    // conflict check above (TODO.md's own named gap, closed 2026-08-12):
    // an event that's chronologically "late" relative to the WHOLE row
    // (because some OTHER property changed more recently) must still fold
    // its own, genuinely-never-touched-before property normally. Before
    // this item, `RouterWorker.FoldAsync` compared `OccurredAt` against
    // the row's single `LastAppliedLogicalTime`, so `renamed` below would
    // have been wrongly rejected in full, purely because `shipped`
    // happened to fold Carrier two hours later -- CustomerName itself was
    // never touched at all before this event.
    public static async Task AnEventLateRelativeToTheWholeRowStillFoldsAPropertyItsOwnPreviousTouchNeverSaw(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-11";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);
        await RegisterOrderCustomerNameUpdated(registry, appId);

        var baseline = DateTimeOffset.UtcNow;
        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-11", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // CustomerName's own PropertyLogicalTimes entry: baseline

        var shipped = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-11", "Carrier": "UPS" }""");
        await SetOccurredAtAsync(db, shipped.CorrelationId, baseline.AddHours(2));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // row.LastAppliedLogicalTime now baseline+2h; Carrier's own entry: baseline+2h; CustomerName's own entry UNCHANGED at baseline

        // Chronologically "late" relative to the ROW (baseline+1h <=
        // baseline+2h) but NOT relative to CustomerName's own last touch
        // (baseline+1h > baseline) -- CustomerName was never touched by
        // the OrderShipped fold above at all.
        var renamed = await Publish(publish, appId, "OrderCustomerNameUpdated", """{ "OrderId": "o-11", "CustomerName": "B. Jones" }""");
        await SetOccurredAtAsync(db, renamed.CorrelationId, baseline.AddHours(1));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var renamedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == renamed.CorrelationId);
        Assert.IsFalse(renamedEvent.LateArrivalFlag, "CustomerName itself was never touched before -- not late on THIS event's own account");

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-11");
        Assert.IsFalse(row.LateArrivalFlag, "every touched property (just CustomerName) applied -- none were late");
        StringAssert.Contains(row.Data, "B. Jones", "CustomerName's own catch-up merges in despite being chronologically 'late' relative to a DIFFERENT property");
        StringAssert.Contains(row.Data, "UPS", "Carrier is untouched by this fold at all, and stays whatever OrderShipped already set");
    }

    public static async Task PublishingWithoutExpectedVersionAppliesUnconditionallyWithNoConflictDetection(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-8";
        await RegisterOrderPlaced(registry, appId);
        await RegisterOrderShipped(registry, appId);

        await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-8", "CustomerName": "A. Smith", "Amount": 42.00 }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var shipped = await Publish(publish, appId, "OrderShipped", """{ "OrderId": "o-8", "Carrier": "UPS" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == shipped.CorrelationId);
        Assert.IsFalse(storedEvent.ConflictFlag);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-8");
        Assert.AreEqual(2, row.Version);
    }

    public static async Task ASchemaInvalidPublishPersistsWith202AndSchemaStatusInvalidAndKnownPropertiesStillFold(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-9";
        await RegisterOrderPlaced(registry, appId);

        var accepted = await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-9", "CustomerName": "A. Smith", "Amount": "not-a-number" }""");
        Assert.AreEqual("received", accepted.Status, "never rejected -- ADR-023");

        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        Assert.AreEqual("invalid", storedEvent.SchemaStatus);
        Assert.AreEqual("applied", storedEvent.Status);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:order:o-9");
        StringAssert.Contains(row.Data, "o-9");
        StringAssert.Contains(row.Data, "A. Smith");
        Assert.IsFalse(row.Data.Contains("not-a-number"), "the individually-invalid Amount must not fold into Data");
    }

    // ADR-020's own "declared version, not active, governs schema validation"
    // guarantee, re-exercised here (not in PublishScenarioAssertions.cs) since
    // it's now a Router-level, not a publish-time, concern (ADR-023).
    public static async Task PublishingAgainstADeclaredVersionBehindTheActiveOneStillValidatesAgainstTheDeclaredVersion(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "entity-demo-10";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["OrderId", "Amount", "Status"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["OrderId", "Amount", "Status", "Currency"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        // v2 is now active and requires "Currency" -- a v1-shaped payload declaring
        // schemaVersion 1 explicitly must still validate against v1, not "whichever is active".

        var accepted = await Publish(publish, appId, "OrderPlaced", """{ "OrderId": "o-10", "Amount": 150.00, "Status": "Paid" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        Assert.AreEqual("conformant", storedEvent.SchemaStatus);
    }
}
