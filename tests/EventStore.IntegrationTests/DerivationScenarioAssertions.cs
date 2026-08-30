using System.Text.Json.Nodes;
using EventStore.Derivation;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Derived/Materialized Event Types (deferred)"
// (docs/08-build-plan.md, ADR-007). No feature doc exists yet for this
// item (it was Deferred, per that build-plan item's own Exit criteria) --
// these scenarios exercise DerivationRegistrationService (registration,
// $on/$select parsing, cycle detection) and DerivationWorker.RunOnceAsync
// (FireOnce/ContinuousEnrichment join semantics, TTL sweep, hop-count cap)
// directly, the same way every other item's tests exercise the underlying
// service rather than the ASP.NET host.
//
// Every scenario uses its own uniquely-suffixed source/derived type names
// (OrderPlaced5/PaymentReceived5/OrderPaid5, ...), not shared ones -- the
// worker matches source events by bare EventType with no AppId scoping
// (docs/10-open-questions.md row 1's pre-existing gap), and this test class
// shares one db across every scenario, so an earlier scenario's
// still-active derivation would otherwise also pick up a later scenario's
// same-named events and emit its own duplicate "orderpaid", breaking
// single-result assertions. Unique names side-step that entirely.
internal static class DerivationScenarioAssertions
{
    private const string OrderPlacedSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["OrderId", "Amount"] }
        """;
    private const string PaymentReceivedSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "Method": { "type": "string" } }, "required": ["OrderId", "Method"] }
        """;

    private static async Task RegisterOrderPlacedAndPaymentReceived(SchemaRegistryService registry, string appId, string suffix)
    {
        await registry.RegisterAsync($"OrderPlaced{suffix}", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: OrderPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await registry.RegisterAsync($"PaymentReceived{suffix}", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: PaymentReceivedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
    }

    private static RegisterDerivationRequest OrderPaidRequest(
        string appId, string suffix, string joinTriggerMode = "FireOnce", string backfillMode = "FromHistory", int? ttlSeconds = 60) =>
        new(
            AppId: appId,
            From: [$"OrderPlaced{suffix}", $"PaymentReceived{suffix}"],
            On: $"OrderPlaced{suffix}/OrderId eq PaymentReceived{suffix}/OrderId",
            Select: $"OrderId:OrderPlaced{suffix}/OrderId,Amount:OrderPlaced{suffix}/Amount,Method:PaymentReceived{suffix}/Method",
            JoinTriggerMode: joinTriggerMode,
            BackfillMode: backfillMode,
            BackfillThroughDerivedSources: true,
            PendingJoinTtlSeconds: ttlSeconds,
            MaxHopCount: 5);

    public static async Task RegisteringAValidDerivationSucceeds(SchemaRegistryService registry, DerivationRegistrationService derivationRegistry)
    {
        const string appId = "derivation-demo-1";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "1");

        var result = await derivationRegistry.RegisterAsync("OrderPaid1", OrderPaidRequest(appId, "1"));

        Assert.IsInstanceOfType<RegisterDerivationResult.Success>(result);
        var definition = await registry.GetActiveAsync(appId, "OrderPaid1");
        Assert.IsNotNull(definition);
        // Auto-composed from $select against the sources' own registered schemas (ADR-007) --
        // Amount copies OrderPlaced1's "number" type, not a generic fallback.
        var schema = JsonNode.Parse(definition.JsonSchema)!;
        Assert.AreEqual("number", schema["properties"]!["Amount"]!["type"]!.GetValue<string>());
    }

    public static async Task RegisteringWithAnUnregisteredSourceFails(SchemaRegistryService registry, DerivationRegistrationService derivationRegistry)
    {
        const string appId = "derivation-demo-2";
        await registry.RegisterAsync("OrderPlaced2", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: OrderPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var result = await derivationRegistry.RegisterAsync("OrderPaid2", OrderPaidRequest(appId, "2"));

        Assert.IsInstanceOfType<RegisterDerivationResult.ValidationFailed>(result);
    }

    public static async Task RegisteringWithAnOnClauseReferencingAnUndeclaredSourceFails(SchemaRegistryService registry, DerivationRegistrationService derivationRegistry)
    {
        const string appId = "derivation-demo-3";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "3");

        var request = OrderPaidRequest(appId, "3") with { On = "OrderPlaced3/OrderId eq ShippingUpdated3/OrderId" };
        var result = await derivationRegistry.RegisterAsync("OrderPaid3", request);

        Assert.IsInstanceOfType<RegisterDerivationResult.ValidationFailed>(result);
    }

    public static async Task RegisteringADerivationDefinitionCycleIsRejected(SchemaRegistryService registry, DerivationRegistrationService derivationRegistry)
    {
        const string appId = "derivation-demo-4";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "4");
        var first = await derivationRegistry.RegisterAsync("OrderPaid4", OrderPaidRequest(appId, "4"));
        Assert.IsInstanceOfType<RegisterDerivationResult.Success>(first);

        // OrderPaid4 is itself now derived from OrderPlaced4+PaymentReceived4; registering
        // OrderPlaced4 as (transitively) derived from OrderPaid4 would close a cycle.
        var cyclic = await derivationRegistry.RegisterAsync("OrderPlaced4", new RegisterDerivationRequest(
            AppId: appId, From: ["OrderPaid4"], On: "OrderPaid4/OrderId eq OrderPaid4/OrderId",
            Select: "OrderId:OrderPaid4/OrderId", JoinTriggerMode: "FireOnce", BackfillMode: "FromNow",
            BackfillThroughDerivedSources: true, PendingJoinTtlSeconds: 60, MaxHopCount: 5));

        Assert.IsInstanceOfType<RegisterDerivationResult.ValidationFailed>(cyclic);
    }

    public static async Task FireOnceEmitsOnceAllSourcesArriveWithParentEventIdsAndHopCount(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-5";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "5");
        Assert.IsInstanceOfType<RegisterDerivationResult.Success>(await derivationRegistry.RegisterAsync("OrderPaid5", OrderPaidRequest(appId, "5")));

        var orderPlaced = await publish.PublishAsync("OrderPlaced5", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-5", "Amount": 42.5 }""", null, null), TestClaimsPrincipal.None);
        var orderPlacedCreated = AssertCreated(orderPlaced);

        await DerivationWorker.RunOnceAsync(db, registry, publish);
        Assert.IsFalse(await db.Events.AnyAsync(e => e.EventType == "orderpaid5")); // only one of two sources has arrived so far

        var paymentReceived = await publish.PublishAsync("PaymentReceived5", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-5", "Method": "CreditCard" }""", null, null), TestClaimsPrincipal.None);
        var paymentReceivedCreated = AssertCreated(paymentReceived);

        await DerivationWorker.RunOnceAsync(db, registry, publish);

        var derived = await db.Events.AsNoTracking().SingleAsync(e => e.EventType == "orderpaid5");
        var payload = JsonNode.Parse(derived.Payload)!;
        Assert.AreEqual("ord-5", payload["OrderId"]!.GetValue<string>());
        Assert.AreEqual(42.5, payload["Amount"]!.GetValue<double>());
        Assert.AreEqual("CreditCard", payload["Method"]!.GetValue<string>());
        Assert.AreEqual(1, derived.DerivationHopCount);

        var parentIds = await db.EventParents.Where(p => p.ChildEventId == derived.EventId).Select(p => p.ParentEventId).ToListAsync();
        CollectionAssert.AreEquivalent(
            new[] { orderPlacedCreated.CorrelationId, paymentReceivedCreated.CorrelationId }, parentIds);

        // The completed join's PendingJoinState row is removed, not left behind.
        Assert.IsFalse(await db.PendingJoinStates.AnyAsync(p => p.DerivationName == "orderpaid5"));
    }

    public static async Task FireOncePendingJoinSurvivesUntilTheRemainingSourceArrives(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-6";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "6");
        await derivationRegistry.RegisterAsync("OrderPaid6", OrderPaidRequest(appId, "6"));

        await publish.PublishAsync("OrderPlaced6", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-6", "Amount": 10 }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);

        var pending = await db.PendingJoinStates.AsNoTracking().SingleAsync(p => p.DerivationName == "orderpaid6");
        Assert.IsNull(pending.ExpiredReason);
        StringAssert.Contains(pending.ArrivedSourcesJson, "orderplaced6");
        Assert.IsFalse(pending.ArrivedSourcesJson.Contains("paymentreceived6", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task ExpiredPendingJoinIsSweptWithARecordedReasonAndNeverEmits(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-7";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "7");
        // A 1-second TTL so the delayed sweep below already treats it as expired.
        await derivationRegistry.RegisterAsync("OrderPaid7", OrderPaidRequest(appId, "7", ttlSeconds: 1));

        await publish.PublishAsync("OrderPlaced7", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-7", "Amount": 10 }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await DerivationWorker.RunOnceAsync(db, registry, publish);

        var pending = await db.PendingJoinStates.AsNoTracking().SingleAsync(p => p.DerivationName == "orderpaid7");
        Assert.AreEqual("ttl_expired", pending.ExpiredReason);

        // The straggling second source arriving after expiry starts a fresh pending
        // join rather than resurrecting the expired one -- it does not retroactively
        // complete the dropped join.
        await publish.PublishAsync("PaymentReceived7", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-7", "Method": "Cash" }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);

        Assert.IsFalse(await db.Events.AnyAsync(e => e.EventType == "orderpaid7"));
        Assert.AreEqual(2, await db.PendingJoinStates.CountAsync(p => p.DerivationName == "orderpaid7"));
    }

    public static async Task ContinuousEnrichmentReEmitsOnEveryNewArrivalOnceBothSourcesHaveArrivedOnce(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-8";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "8");
        await derivationRegistry.RegisterAsync("OrderPaid8", OrderPaidRequest(appId, "8", joinTriggerMode: "ContinuousEnrichment"));

        await publish.PublishAsync("OrderPlaced8", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-8", "Amount": 10 }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);
        Assert.AreEqual(0, await db.Events.CountAsync(e => e.EventType == "orderpaid8"));

        await publish.PublishAsync("PaymentReceived8", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-8", "Method": "CreditCard" }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);
        Assert.AreEqual(1, await db.Events.CountAsync(e => e.EventType == "orderpaid8"));

        // A second OrderPlaced8 arrival for the same key re-emits, enriched against
        // PaymentReceived8's current latest state -- no PendingJoinState involved.
        await publish.PublishAsync("OrderPlaced8", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-8", "Amount": 99 }""", null, null), TestClaimsPrincipal.None);
        await DerivationWorker.RunOnceAsync(db, registry, publish);

        var derivedEvents = await db.Events.AsNoTracking().Where(e => e.EventType == "orderpaid8").OrderBy(e => e.SequenceNumber).ToListAsync();
        Assert.HasCount(2, derivedEvents);
        Assert.AreEqual(99, JsonNode.Parse(derivedEvents[1].Payload)!["Amount"]!.GetValue<double>());
        Assert.AreEqual(0, await db.PendingJoinStates.CountAsync(p => p.DerivationName == "orderpaid8"));
    }

    public static async Task BackfillFromNowIgnoresEventsPublishedBeforeRegistration(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-9";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "9");

        // Published before the derivation is even registered.
        await publish.PublishAsync("OrderPlaced9", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-9-old", "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("PaymentReceived9", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-9-old", "Method": "Cash" }""", null, null), TestClaimsPrincipal.None);

        await derivationRegistry.RegisterAsync("OrderPaid9", OrderPaidRequest(appId, "9", backfillMode: "FromNow"));

        await publish.PublishAsync("OrderPlaced9", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-9-new", "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("PaymentReceived9", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-9-new", "Method": "Cash" }""", null, null), TestClaimsPrincipal.None);

        await DerivationWorker.RunOnceAsync(db, registry, publish);

        var derivedKeys = await db.Events.AsNoTracking().Where(e => e.EventType == "orderpaid9").Select(e => e.Payload).ToListAsync();
        Assert.HasCount(1, derivedKeys);
        Assert.AreEqual("ord-9-new", JsonNode.Parse(derivedKeys[0])!["OrderId"]!.GetValue<string>());
    }

    private const string LineItemPlacedSchema = """
        { "type": "object", "properties": { "OrderId": { "type": "string" }, "Quantity": { "type": "number" }, "UnitPrice": { "type": "number" } }, "required": ["OrderId", "Quantity", "UnitPrice"] }
        """;

    public static async Task CalculatedFieldEvaluatesAnExpressionOverArrivedSources(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-11";
        var suffix = "11";
        await registry.RegisterAsync($"LineItemPlaced{suffix}", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: LineItemPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        // A single-source derivation, trivially self-joined (same technique
        // RegisteringADerivationDefinitionCycleIsRejected already uses) -- the
        // calculated field itself doesn't need a join, just a source to read
        // arrived fields from.
        var request = new RegisterDerivationRequest(
            AppId: appId,
            From: [$"LineItemPlaced{suffix}"],
            On: $"LineItemPlaced{suffix}/OrderId eq LineItemPlaced{suffix}/OrderId",
            // double(...) on Quantity is required, not stylistic: CEL has no
            // implicit int/double coercion (unlike JS-style arithmetic), and a
            // whole-number JSON value like "Quantity": 3 round-trips as CEL's
            // int, not double -- int * double fails to compile at evaluation
            // time ("no such overload: int.multiply(double)"), found by
            // actually running this expression, not assumed from the CEL docs.
            Select: $"Quantity:LineItemPlaced{suffix}/Quantity,UnitPrice:LineItemPlaced{suffix}/UnitPrice," +
                    $"Total:=double(event.lineitemplaced{suffix}.Quantity) * event.lineitemplaced{suffix}.UnitPrice",
            JoinTriggerMode: "FireOnce",
            BackfillMode: "FromHistory",
            BackfillThroughDerivedSources: true,
            PendingJoinTtlSeconds: 60,
            MaxHopCount: 5);
        Assert.IsInstanceOfType<RegisterDerivationResult.Success>(await derivationRegistry.RegisterAsync($"InvoiceLine{suffix}", request));

        await publish.PublishAsync($"LineItemPlaced{suffix}", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-11", "Quantity": 3, "UnitPrice": 12.5 }""", null, null), TestClaimsPrincipal.None);

        await DerivationWorker.RunOnceAsync(db, registry, publish, expressionEvaluator: UpcastingTestSupport.CreateEvaluator());

        var derived = await db.Events.AsNoTracking().SingleAsync(e => e.EventType == $"invoiceline{suffix}");
        var payload = JsonNode.Parse(derived.Payload)!;
        Assert.AreEqual(3, payload["Quantity"]!.GetValue<double>());
        Assert.AreEqual(12.5, payload["UnitPrice"]!.GetValue<double>());
        Assert.AreEqual(37.5, payload["Total"]!.GetValue<double>());
    }

    public static async Task RegisteringACalculatedFieldWithAnUncompilableExpressionFails(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry)
    {
        const string appId = "derivation-demo-12";
        var suffix = "12";
        await registry.RegisterAsync($"LineItemPlaced{suffix}", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: LineItemPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var request = new RegisterDerivationRequest(
            AppId: appId,
            From: [$"LineItemPlaced{suffix}"],
            On: $"LineItemPlaced{suffix}/OrderId eq LineItemPlaced{suffix}/OrderId",
            Select: "Total:=event.Quantity *", // deliberately incomplete -- must fail TryCompile, not reach the worker
            JoinTriggerMode: "FireOnce",
            BackfillMode: "FromHistory",
            BackfillThroughDerivedSources: true,
            PendingJoinTtlSeconds: 60,
            MaxHopCount: 5);

        var result = await derivationRegistry.RegisterAsync($"InvoiceLine{suffix}", request);

        Assert.IsInstanceOfType<RegisterDerivationResult.ValidationFailed>(result);
    }

    public static async Task HopCountExceedingMaxHopCountSkipsEmissionAndRecordsADeadLetter(
        SchemaRegistryService registry, DerivationRegistrationService derivationRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "derivation-demo-10";
        await RegisterOrderPlacedAndPaymentReceived(registry, appId, "10");
        var request = OrderPaidRequest(appId, "10") with { MaxHopCount = 0 };
        await derivationRegistry.RegisterAsync("OrderPaid10", request);

        await publish.PublishAsync("OrderPlaced10", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-10", "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync("PaymentReceived10", new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-10", "Method": "Cash" }""", null, null), TestClaimsPrincipal.None);

        await DerivationWorker.RunOnceAsync(db, registry, publish);

        // MaxHopCount 0 means even the first-ever join (hopCount 1) exceeds the cap --
        // emission is skipped, and a dead-letter row records why.
        Assert.IsFalse(await db.Events.AnyAsync(e => e.EventType == "orderpaid10"));
        var deadLetter = await db.PendingJoinStates.AsNoTracking().SingleAsync(p => p.DerivationName == "orderpaid10");
        Assert.AreEqual("hop_limit_exceeded", deadLetter.ExpiredReason);
    }

    private static PublishResult.Accepted AssertCreated(PublishResult result)
    {
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        return (PublishResult.Accepted)result;
    }
}
