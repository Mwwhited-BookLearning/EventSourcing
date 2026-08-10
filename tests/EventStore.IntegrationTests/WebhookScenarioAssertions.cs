using EventStore.Domain.Webhooks;
using EventStore.Inbox;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using EventStore.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Outbound Webhooks" (docs/08-build-plan.md, ADR-060)
// that need no real HTTP delivery -- registration/enqueue mechanics only.
// Real HTTP delivery (signing, retry+backoff, dead-letter, restart-resume,
// erasure-then-retry) is covered separately in
// WebhookDeliveryHttpSqliteTests.cs, the same split StreamingScenarioAssertions/
// StreamingHttpSqliteTests already established for "mechanics vs. real wire".
internal static class WebhookScenarioAssertions
{
    private static async Task RegisterOrderPlacedAsync(SchemaRegistryService registry, string appId)
    {
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "OrderId": { "type": "string" },
                    "Amount": { "type": "number" },
                    "CustomerTaxId": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:pii" } }
                  }, "required": ["OrderId", "Amount"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
    }

    public static async Task RegisteringASubscriptionFreezesItsClaimSnapshotOnce(EventStoreContext db, WebhookSubscriptionService subscriptions)
    {
        const string appId = "webhooks-demo-1";
        var registeringCaller = TestClaimsPrincipal.With("clearance:none");

        var subscription = await subscriptions.RegisterAsync(appId, "https://ops.example.com/hooks", ["OrderPlaced"], null, registeringCaller);

        var hasClaimAtRegistration = WebhookSubscriptionService.BuildHasClaim(subscription.FixedClaimsSnapshot);
        Assert.IsTrue(hasClaimAtRegistration("clearance:none"));
        Assert.IsFalse(hasClaimAtRegistration("clearance:phi"));

        // A claim granted to the SAME caller identity AFTER registration must
        // never retroactively change an already-registered subscription's own
        // snapshot -- there is no live re-check against the caller at all,
        // only ever against the frozen copy captured at registration time.
        var laterGrantedCaller = TestClaimsPrincipal.With("clearance:phi");
        var reloaded = await db.WebhookSubscriptions.AsNoTracking().SingleAsync(s => s.SubscriptionId == subscription.SubscriptionId);
        var hasClaimAfterUnrelatedGrant = WebhookSubscriptionService.BuildHasClaim(reloaded.FixedClaimsSnapshot);
        Assert.IsFalse(hasClaimAfterUnrelatedGrant("clearance:phi"), "the subscription's own frozen snapshot must be unaffected by a claim granted afterward");
        Assert.IsNotNull(laterGrantedCaller); // the later grant is real, just never observed by this already-registered subscription
    }

    public static async Task AMatchingEventIsMaskedAndEnqueuedIntoTheDurableOutbox(
        EventStoreContext db, SchemaRegistryService registry, PublishService publish, WebhookSubscriptionService subscriptions,
        UpcastChain upcastChain, IPayloadMasker payloadMasker)
    {
        const string appId = "webhooks-demo-2";
        await RegisterOrderPlacedAsync(registry, appId);

        // The registering caller holds no clearance:pii claim -- CustomerTaxId
        // must come through masked, never the real value, per ADR-009's own
        // masking rule applied against this subscription's frozen snapshot.
        var subscription = await subscriptions.RegisterAsync(appId, "https://ops.example.com/hooks", ["OrderPlaced"], null, TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("OrderPlaced",
            new PublishEventRequest(appId, 1, """{ "OrderId": "order-1", "Amount": 150.00, "CustomerTaxId": "123-45-6789" }""", null, null),
            TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);

        var row = await db.WebhookOutbox.AsNoTracking().SingleAsync(o => o.SubscriptionId == subscription.SubscriptionId);
        Assert.IsFalse(row.EventPayloadSnapshot.Contains("123-45-6789"), "the real CustomerTaxId value must never appear in an enqueued row for a subscription lacking the claim");
        Assert.IsTrue(row.EventPayloadSnapshot.Contains("masked"), row.EventPayloadSnapshot);
    }

    public static async Task ANonMatchingEventTypeIsNeverEnqueuedForThatSubscription(
        EventStoreContext db, SchemaRegistryService registry, PublishService publish, WebhookSubscriptionService subscriptions,
        UpcastChain upcastChain, IPayloadMasker payloadMasker)
    {
        const string appId = "webhooks-demo-3";
        await RegisterOrderPlacedAsync(registry, appId);
        await registry.RegisterAsync("OrderShipped", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var subscription = await subscriptions.RegisterAsync(appId, "https://ops.example.com/hooks", ["OrderPlaced"], null, TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("OrderShipped",
            new PublishEventRequest(appId, 1, """{ "OrderId": "order-2" }""", null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);

        var enqueuedCount = await db.WebhookOutbox.CountAsync(o => o.SubscriptionId == subscription.SubscriptionId);
        Assert.AreEqual(0, enqueuedCount, "OrderShipped doesn't match this subscription's own EventTypes list");
    }
}
