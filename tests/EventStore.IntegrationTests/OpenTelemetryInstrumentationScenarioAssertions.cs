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

// Shared scenarios for "Mechanism-Level OpenTelemetry Instrumentation"
// (docs/08-build-plan.md, ADR-088) -- exercises the Router fold-lag
// histogram, the hash-chain verification counter, and the webhook
// delivery-lag histogram directly against their own real mechanism
// entry points, the same "exercise the mechanics directly" pattern every
// other item in this build stage already uses. The peer-sync outbox
// depth/age gauges need a real two-site HTTP round trip to exercise
// PeerSyncWorker.SyncOnceWithAsync's own tick (not just PeerSyncReceiver
// directly, which every existing replication test already uses) -- that
// one scenario lives in ReplicationHttpSqliteTests.cs instead, reusing
// its own already-built two-Host fixture rather than duplicating it here.
internal static class OpenTelemetryInstrumentationScenarioAssertions
{
    public static async Task AnAcceptedPublishRecordsRouterFoldLagAndANamedFoldActivity(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "otel-demo-router-1";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var (meterListener, measurements) = OpenTelemetryTestSupport.ListenForDoubleInstrument("duplex.router.fold_lag");
        using var _ = meterListener;
        var (activityListener, activities) = OpenTelemetryTestSupport.ListenForActivity("duplex.router.fold");
        using var __ = activityListener;

        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "otel-1" }""", null, null), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var forThisApp = measurements.Where(m => m.HasTag("app.id", appId)).ToList();
        Assert.HasCount(1, forThisApp, "an ordinary accepted publish folds immediately -- exactly one fold-lag measurement, tagged with this scenario's own AppId");
        Assert.IsGreaterThanOrEqualTo(0.0, forThisApp[0].Value);
        Assert.IsGreaterThanOrEqualTo(1, activities.Count, "the fold step must produce a named Activity, a distinct assertion from the metric recording");
    }

    // ADR-088's own explicit warning: an event gated through
    // unattested/pending_review waits on open-ended human review, not
    // processing time, and must never be conflated into the fold-lag
    // histogram -- the negative half of the scenario directly above.
    public static async Task AReviewPendingPublishRecordsNoRouterFoldLagAtAll(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "otel-demo-router-2";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var (meterListener, measurements) = OpenTelemetryTestSupport.ListenForDoubleInstrument("duplex.router.fold_lag");
        using var _ = meterListener;

        await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, """{ "OrderId": "otel-2" }""", null, null, ReviewPending: true), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        Assert.IsFalse(measurements.Any(m => m.HasTag("app.id", appId)), "AuthorityStatus never reached \"accepted\" here -- the authoritative fold, and therefore the histogram, must never fire");
    }

    public static async Task VerifyingACleanChainRecordsAVerifiedOutcomeAndANamedActivity(
        SchemaRegistryService registry, PublishService publish, ChainVerificationService verifier)
    {
        const string appId = "otel-demo-hashchain-1";
        await registry.RegisterAsync("ChainedType", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var accepted = (PublishResult.Accepted)await publish.PublishAsync(
            "ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);

        var (meterListener, measurements) = OpenTelemetryTestSupport.ListenForLongInstrument("duplex.hashchain.verification_outcomes");
        using var _ = meterListener;
        var (activityListener, activities) = OpenTelemetryTestSupport.ListenForActivity("duplex.hashchain.verify");
        using var __ = activityListener;

        var result = await verifier.VerifyAsync(accepted.SequenceNumber);

        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(result);
        Assert.IsTrue(measurements.Any(m => m.HasTag("outcome", "verified")), "a clean verification must increment the counter tagged outcome=verified");
        Assert.IsFalse(measurements.Any(m => m.HasTag("outcome", "tampered")));
        Assert.IsGreaterThanOrEqualTo(1, activities.Count);
    }

    public static async Task VerifyingATamperedChainRecordsATamperedOutcome(
        SchemaRegistryService registry, PublishService publish, ChainVerificationService verifier, EventStoreContext db)
    {
        const string appId = "otel-demo-hashchain-2";
        await registry.RegisterAsync("ChainedType", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var accepted = (PublishResult.Accepted)await publish.PublishAsync(
            "ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);

        var row = await db.Events.SingleAsync(e => e.EventId == accepted.CorrelationId);
        row.Payload = """{ "Amount": 999 }"""; // test-only direct edit, PayloadHash deliberately left stale
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (meterListener, measurements) = OpenTelemetryTestSupport.ListenForLongInstrument("duplex.hashchain.verification_outcomes");
        using var _ = meterListener;

        var result = await verifier.VerifyAsync(accepted.SequenceNumber);

        Assert.IsInstanceOfType<ChainVerificationResult.Tampered>(result);
        Assert.IsTrue(measurements.Any(m => m.HasTag("outcome", "tampered")), "a divergent chain must increment the counter tagged outcome=tampered");
        Assert.IsFalse(measurements.Any(m => m.HasTag("outcome", "verified")));
    }

    public static async Task AConfirmedWebhookDeliveryRecordsDeliveryLagAndANamedPumpActivity(
        EventStoreContext db, SchemaRegistryService registry, PublishService publish, WebhookSubscriptionService subscriptions,
        UpcastChain upcastChain, IPayloadMasker payloadMasker, HttpClient httpClient, string backendAddress)
    {
        const string appId = "otel-demo-webhook-1";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        await subscriptions.RegisterAsync(appId, backendAddress, ["OrderPlaced"], null, TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("OrderPlaced",
            new PublishEventRequest(appId, 1, """{ "OrderId": "otel-webhook-1" }""", null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);

        var (meterListener, measurements) = OpenTelemetryTestSupport.ListenForDoubleInstrument("duplex.webhook.delivery_lag");
        using var _ = meterListener;
        var (activityListener, activities) = OpenTelemetryTestSupport.ListenForActivity("duplex.webhook.delivery_pump");
        using var __ = activityListener;

        var options = new WebhookOptions();
        var retryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);

        var forThisApp = measurements.Where(m => m.HasTag("app.id", appId)).ToList();
        Assert.HasCount(1, forThisApp, "a confirmed delivery must record exactly one delivery-lag measurement, tagged with this scenario's own AppId");
        Assert.IsGreaterThanOrEqualTo(0.0, forThisApp[0].Value);
        Assert.IsGreaterThanOrEqualTo(1, activities.Count);
    }
}
