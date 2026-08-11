using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Non-Authoritative Capture" (docs/08-build-plan.md),
// mirroring docs/features/non-authoritative-capture.md's own Gherkin.
// AuthorityStatus/AuthorityDecisionRef/LiveEntityStoreRow have no HTTP query
// surface yet (GraphQL doesn't exist until "GraphQL-Only Query Layer" --
// this doc's own GraphQL shapes are explicitly "illustrative only"), so
// this exercises the fold mechanics directly against `db.EntityStore`/
// `db.LiveEntityStore`, the same "exercise the mechanics directly" pattern
// every other *ScenarioAssertions.cs file in this repo already establishes.
internal static class NonAuthoritativeCaptureScenarioAssertions
{
    private static Task RegisterSensorReading(SchemaRegistryService registry, string appId, string rejectionBehavior = "Annotate") =>
        registry.RegisterAsync("SensorReading", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "SensorId": { "type": "string" }, "Reading": { "type": "number" } }, "required": ["SensorId", "Reading"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.SensorId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null,
            RejectionBehavior: rejectionBehavior));

    private static Task RegisterClaimSubmission(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("ClaimSubmission", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "ClaimId": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["ClaimId", "Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.ClaimId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null,
            RejectionBehavior: "Compensate"));

    private static Task RegisterAuthorityDecision(SchemaRegistryService registry, string appId) =>
        registry.RegisterAsync("authorityDecision", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "targetEventId": { "type": "string" }, "decision": { "type": "string" }, "decidingActorId": { "type": "string" }, "reason": { "type": "string" } }, "required": ["targetEventId", "decision", "decidingActorId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.targetEventId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task PublishingAnEventWithAttestedClaimsPersistsAsUnattestedNeverBlockingIngestion(
        SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "authority-demo-1";
        await RegisterSensorReading(registry, appId);

        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading",
            new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 21.5 }""", null, null,
                AttestedActorId: "field-agent-7", AttestedClaims: JsonNode.Parse("""{ "type": "ucan-invocation", "capability": "sensor:report" }""")),
            TestClaimsPrincipal.None);

        Assert.AreEqual("unattested", result.AuthorityStatus, "a self-attested submitter never blocks ingestion, but starts advisory review at unattested");
    }

    public static async Task AnEventWithAnExplicitReviewPendingMarkerPersistsAsPendingReview(
        SchemaRegistryService registry, PublishService publish)
    {
        // ADR-042's second trigger -- a detector's own "not yet validated"
        // marker, distinct from the self-attestation case above (no
        // AttestedClaims/AttestedActorId at all here).
        const string appId = "authority-demo-1b";
        await RegisterSensorReading(registry, appId);

        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading",
            new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-99", "Reading": 5.0 }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);

        Assert.AreEqual("pending_review", result.AuthorityStatus);
    }

    public static async Task AnUnattestedEventReachesTheLiveViewImmediatelyButNotTheAuthoritativeEntityStore(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-2";
        await RegisterSensorReading(registry, appId);

        await publish.PublishAsync(
            "SensorReading",
            new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 21.5 }""", null, null, AttestedActorId: "field-agent-7"),
            TestClaimsPrincipal.None);

        var upcastChain = UpcastingTestSupport.CreateChain();
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.AreEqual("unattested", liveRow.AuthorityStatus);
        Assert.IsTrue(liveRow.Data.Contains("21.5"), "the Live View folds every event immediately, no AuthorityStatus gate");

        var authoritativeRow = await db.EntityStore.AsNoTracking().SingleOrDefaultAsync(r => r.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.IsNull(authoritativeRow, "the authoritative Entity Store gets no row at all until AuthorityStatus reaches accepted");
    }

    public static async Task OnceAcceptedTheAuthoritativeEntityStoreCatchesUpToWhatTheLiveViewAlreadyShowed(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-3";
        await RegisterSensorReading(registry, appId);
        await RegisterAuthorityDecision(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        var reading = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading",
            new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 45.0 }""", null, null, AttestedActorId: "field-agent-7"),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var authoritativeBefore = await db.EntityStore.AsNoTracking().SingleOrDefaultAsync(r => r.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.IsNull(authoritativeBefore);

        await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{reading.CorrelationId}}", "decision": "accepted", "decidingActorId": "reviewer-1" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var authoritativeAfter = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.IsTrue(authoritativeAfter.Data.Contains("45"), "the authoritative store catches up to what the Live View already showed");

        var targetAfter = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == reading.CorrelationId);
        Assert.AreEqual("accepted", targetAfter.AuthorityStatus);
    }

    public static async Task AuthorityStatusIsIndependentOfSchemaStatus(SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-4";
        await RegisterSensorReading(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        // Missing the required "Reading" property -- SchemaStatus: invalid --
        // while still carrying AttestedActorId -- AuthorityStatus: unattested.
        // Neither flag blocks persistence; the two axes are independent.
        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading",
            new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42" }""", null, null, AttestedActorId: "field-agent-7"),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.AreEqual("invalid", stored.SchemaStatus);
        Assert.AreEqual("unattested", stored.AuthorityStatus);
    }

    public static async Task AnAuthorityDecisionRejectedEventOnAnAnnotateTypeEventFlagsWithoutTouchingPayload(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-5";
        await RegisterSensorReading(registry, appId, rejectionBehavior: "Annotate");
        await RegisterAuthorityDecision(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        // Plain authenticated publish (no AttestedClaims) -- AuthorityStatus
        // defaults to accepted and folds normally (ADR-042).
        var reading = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading", new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 99.9 }""", null, null), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{reading.CorrelationId}}", "decision": "rejected", "decidingActorId": "reviewer-1", "reason": "sensor miscalibrated" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == reading.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsTrue(target.Payload.Contains("99.9"), "Payload is never mutated, only flagged");

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.IsTrue(row.Data.Contains("99.9"), "Annotate leaves the already-folded authoritative state exactly as it was");

        var decisionEntityCount = await db.Events.AsNoTracking().CountAsync(e => e.EventType == "sensorreading" && e.EntityId == $"{appId}:sensorreading:sensor-42");
        Assert.AreEqual(1, decisionEntityCount, "no compensating patch event was appended for an Annotate-type rejection");
    }

    public static async Task AnAuthorityDecisionRejectedEventOnACompensateTypeEventTriggersACompensatingPatch(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-6";
        await RegisterClaimSubmission(registry, appId);
        await RegisterAuthorityDecision(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        var claim = (PublishResult.Accepted)await publish.PublishAsync(
            "ClaimSubmission",
            new PublishEventRequest(appId, 1, """{ "ClaimId": "claim-9", "Amount": 5000 }""", null, null, AttestedActorId: "field-agent-7"),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        // Accept it first -- Compensate only matters for the residual case
        // ADR-042 narrows this fork to: already accepted and folded, now reversed.
        await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{claim.CorrelationId}}", "decision": "accepted", "decidingActorId": "reviewer-1" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var rowAfterAccept = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:claimsubmission:claim-9");
        Assert.IsTrue(rowAfterAccept.Data.Contains("5000"));

        await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{claim.CorrelationId}}", "decision": "rejected", "decidingActorId": "reviewer-1", "reason": "unverifiable claimant" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == claim.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsTrue(target.Payload.Contains("5000"), "the original event's own Payload is still never mutated");

        var compensatingCount = await db.Events.AsNoTracking().CountAsync(e => e.EntityId == $"{appId}:claimsubmission:claim-9" && e.EventId != claim.CorrelationId);
        Assert.AreEqual(1, compensatingCount, "exactly one new compensating patch event was appended, never a mutation of the rejected one");

        var rowAfterReject = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{appId}:claimsubmission:claim-9");
        Assert.IsFalse(rowAfterReject.Data.Contains("5000"), "the compensating patch reverted the authoritative Entity Store's Amount");
    }

    public static async Task AuthorityDecisionRefDenormalizesBackToTheDecidingEvent(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-7";
        await RegisterSensorReading(registry, appId);
        await RegisterAuthorityDecision(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        var reading = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading", new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 12.0 }""", null, null), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{reading.CorrelationId}}", "decision": "rejected", "decidingActorId": "reviewer-1" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == reading.CorrelationId);
        Assert.AreEqual(decision.CorrelationId, target.AuthorityDecisionRef);
    }

    // "Two servers independently disagreeing about review status resolves
    // via ConflictFlag" -- the cross-site wire/fold mechanism itself is
    // already proven generically by ReplicationScenarioAssertions; this
    // exercises the AuthorityDecisionResolver-specific detection logic the
    // same way: a second decision event, inserted directly via
    // EventAppender the same way PeerSyncReceiver bypasses PublishService
    // for an already-once-validated event arriving from elsewhere.
    public static async Task TwoServersIndependentlyDisagreeingAboutReviewStatusResolvesViaConflictFlag(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "authority-demo-8";
        await RegisterSensorReading(registry, appId);
        await RegisterAuthorityDecision(registry, appId);
        var upcastChain = UpcastingTestSupport.CreateChain();

        var reading = (PublishResult.Accepted)await publish.PublishAsync(
            "SensorReading", new PublishEventRequest(appId, 1, """{ "SensorId": "sensor-42", "Reading": 30.0 }""", null, null), TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decisionA = (PublishResult.Accepted)await publish.PublishAsync(
            "authorityDecision",
            new PublishEventRequest(appId, 1, $$"""{ "targetEventId": "{{reading.CorrelationId}}", "decision": "accepted", "decidingActorId": "reviewer-a" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        // Simulates decision-b arriving from a peer that never saw
        // decision-a -- appended directly, bypassing PublishService, the
        // same posture PeerSyncReceiver already uses for an event that
        // already passed its own checks once, elsewhere.
        var decisionBPayload = $$"""{ "targetEventId": "{{reading.CorrelationId}}", "decision": "rejected", "decidingActorId": "reviewer-b" }""";
        var decisionB = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            AppId = appId,
            EntityId = "",
            EventType = "authoritydecision",
            SchemaVersion = 1,
            Payload = decisionBPayload,
            PayloadHash = EventPayloadHash.Compute("authoritydecision", decisionBPayload, []),
            ChainHash = "",
            Status = "received",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "site-b-reviewer",
            OriginId = "site-b",
        };
        await EventAppender.AppendAsync(db, decisionB, []);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decisionBStored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == decisionB.EventId);
        Assert.IsTrue(decisionBStored.ConflictFlag, "decision-b is applied SECOND against an already-decided target");

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == reading.CorrelationId);
        Assert.AreEqual(decisionB.EventId, target.AuthorityDecisionRef, "the fold step applies decision-b without blocking or rejecting it -- last applied wins, not merged or auto-resolved");
        Assert.AreEqual("rejected", target.AuthorityStatus);
    }
}
