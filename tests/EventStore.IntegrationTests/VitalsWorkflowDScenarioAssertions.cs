using EventStore.Domain.Streaming;
using EventStore.ExpectedResponse;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Vitals;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Vitals proving-ground sample's Workflow D --
// Intraoperative Monitoring & Alert Response (docs/domains/clinical-
// trials-device-telemetry/features/intraoperative-monitoring-and-alert-
// response.md) -- ADR-094's first real domain-level exercise. Runs both
// RouterWorker.RunOnceAsync and ExpectedResponseWatcher.RunOnceAsync
// every tick, the same "drive both static entry points directly" pattern
// item 49's own core-engine tests already established.
internal static class VitalsWorkflowDScenarioAssertions
{
    private const string AppId = VitalsWorkflowD.AppId;

    // Escalation scenarios need a Within short enough not to actually
    // sleep for the domain's real 2-minute clinical window -- registers
    // a fresh, later version of IonmAlertRaised with a 1ms window instead,
    // the identical "escalation-specific override" ADR-094's own core
    // ExpectedResponseScenarioAssertions.cs already uses.
    // The short-window IonmAlertRaised registration below must stay the
    // ACTIVE version when these scenarios publish -- calling
    // VitalsWorkflowD.RegisterAsync afterward would register a THIRD
    // version with the real 2-minute Within, overriding it. Registers
    // IonmAlertAcknowledged + the shared authorityDecision claim directly
    // instead, skipping VitalsWorkflowD's own IonmAlertRaised call.
    private static async Task RegisterWithShortAcknowledgmentWindowAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        await registry.RegisterAsync("IonmAlertRaised", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: """{ "type": "object", "properties": { "AlertId": { "type": "string" }, "SubjectId": { "type": "string" }, "Finding": { "type": "string" }, "Severity": { "type": "string" } }, "required": ["AlertId", "SubjectId", "Finding", "Severity"] }""",
            FilterableFields: [], ChangeKind: "Partial", EntityIdField: "$.AlertId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "IonmAlert",
            ExpectedResponse: new ExpectedResponseRequest("IonmAlertAcknowledged", TimeSpan.FromMilliseconds(1))), ct);

        await registry.RegisterAsync("IonmAlertAcknowledged", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: """{ "type": "object", "properties": { "AlertId": { "type": "string" }, "AckedBy": { "type": "string" } }, "required": ["AlertId", "AckedBy"] }""",
            FilterableFields: [], ChangeKind: "Partial", EntityIdField: "$.AlertId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "IonmAlert"), ct);

        await VitalsSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "review:ionm", ct);
    }

    private static async Task RunOneTickAsync(EventStoreContext db, SchemaRegistryService registry, PublishService publish)
    {
        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);
    }

    public static async Task ADetectorsAlertIsCapturedNonAuthoritativelyCarryingATelemetryPointerAndStartsATrackedExpectation(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);
        var pointer = new List<TelemetryPointerEntry> { new("ionm-s0091-fast", null, DateTimeOffset.Parse("2026-08-04T09:14:02Z"), null) };

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null,
                TelemetryPointer: pointer, ReviewPending: true),
            TestClaimsPrincipal.None);
        Assert.AreEqual("pending_review", alert.AuthorityStatus);

        var alertEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == alert.CorrelationId);
        await RunOneTickAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == alert.CorrelationId);
        Assert.AreEqual(alertEvent.AppendedAt + TimeSpan.FromMinutes(2), tracker.DeadlineAt);

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:ionmalert:alert-77");
        Assert.IsTrue(liveRow.Data.Contains("SSEP amplitude drop"));
    }

    public static async Task AnAcknowledgmentWithinTheWindowSatisfiesTheTrackerAndMergesOntoTheSameEntity(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77b", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var ack = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertAcknowledged",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77b", "AckedBy": "tech-4" }""", null, null, RespondsToEventId: alert.CorrelationId),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == alert.CorrelationId);
        Assert.AreEqual(ack.CorrelationId, tracker.SatisfiedByEventId);
        Assert.IsNotNull(tracker.SatisfiedAt);
        Assert.IsNull(tracker.EscalatedAt);

        var missingCount = await db.Events.CountAsync(e => e.AppId == AppId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant() && e.RespondsToEventId == alert.CorrelationId);
        Assert.AreEqual(0, missingCount);

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:ionmalert:alert-77b");
        Assert.IsTrue(liveRow.Data.Contains("tech-4"), "Partial merge (ADR-016) -- Finding/Severity untouched, AckedBy added");
        Assert.IsTrue(liveRow.Data.Contains("Urgent"), "the Full IonmAlertRaised fields survive the later Partial merge");
    }

    public static async Task NoAcknowledgmentByTheDeadlineEscalatesExactlyOnce(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await RegisterWithShortAcknowledgmentWindowAsync(registry);

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77c", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await RunOneTickAsync(db, registry, publish);
        await RunOneTickAsync(db, registry, publish); // a second sweep -- must not double-publish

        var missing = await db.Events
            .Where(e => e.AppId == AppId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant() && e.RespondsToEventId == alert.CorrelationId)
            .ToListAsync();
        Assert.AreEqual(1, missing.Count, "exactly one ExpectedResponseMissing, referencing alert-77c");
    }

    public static async Task ALateAcknowledgmentAfterEscalationIsStillRecordedNeverRejectedAndNeverTriggersASecondEscalation(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await RegisterWithShortAcknowledgmentWindowAsync(registry);

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77d", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await RunOneTickAsync(db, registry, publish); // escalates

        var lateAck = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertAcknowledged",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77d", "AckedBy": "tech-4" }""", null, null, RespondsToEventId: alert.CorrelationId),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == alert.CorrelationId);
        Assert.AreEqual(lateAck.CorrelationId, tracker.SatisfiedByEventId);
        Assert.IsNotNull(tracker.EscalatedAt);

        var missingCount = await db.Events.CountAsync(e => e.AppId == AppId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant() && e.RespondsToEventId == alert.CorrelationId);
        Assert.AreEqual(1, missingCount, "still exactly one, never a second escalation triggered by the late ack");
    }

    public static async Task TheAcknowledgmentAndTheNeurologistsAuthoritativeInterpretationAreIndependentFacts(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77e", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);
        await publish.PublishAsync("IonmAlertAcknowledged",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77e", "AckedBy": "tech-4" }""", null, null, RespondsToEventId: alert.CorrelationId),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == alert.CorrelationId);
        Assert.AreEqual("pending_review", target.AuthorityStatus, "being acknowledged in real time never by itself moves AuthorityStatus -- only a signed authorityDecision does (ADR-035/094 are orthogonal axes)");
    }

    public static async Task TheNeurologistsSignOffWithoutSufficientStepUpIsChallengedNotStored(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);
        var neuro = TestClaimsPrincipal.WithClaims(("sub", "neuro-12"), ("review", "ionm"));

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77f", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{alert.CorrelationId}}", "decision": "accepted", "decidingActorId": "neuro-12" }""", null, null,
                Meaning: "approved"),
            neuro);

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        CollectionAssert.AreEqual(new[] { "urn:trial:step-up" }, stepUp.AcrValues.ToArray());
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "authoritydecision" && e.EntityId == alert.CorrelationId.ToString()));
    }

    public static async Task TheNeurologistSignsOffAcceptedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var neuro = TestClaimsPrincipal.WithClaims(("sub", "neuro-12"), ("review", "ionm"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77g", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);
        await publish.PublishAsync("IonmAlertAcknowledged",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77g", "AckedBy": "tech-4" }""", null, null, RespondsToEventId: alert.CorrelationId),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{alert.CorrelationId}}", "decision": "accepted", "decidingActorId": "neuro-12" }""", null, null,
                Meaning: "approved"),
            neuro);
        await RunOneTickAsync(db, registry, publish);

        var storedDecision = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == decision.CorrelationId);
        Assert.AreEqual("neuro-12", storedDecision.Signature!.SignerId);
        Assert.AreEqual("approved", storedDecision.Signature.Meaning);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == alert.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus);

        // A real, verified finding, not the feature doc's own assumed
        // outcome: IonmAlertAcknowledged's OWN accepted fold already ran
        // (step above) with an OccurredAt LATER than IonmAlertRaised's own
        // (published first, chronologically) -- so by the time the
        // neurologist's decision finally catches IonmAlertRaised UP,
        // ADR-029's late-arrival guard (`storedEvent.OccurredAt <=
        // row.LastAppliedLogicalTime`) rejects this catch-up fold as
        // "late," even though nothing it contributes (Finding/Severity)
        // actually conflicts with AckedBy. That guard is coarse -- per
        // EVENT, not per FIELD -- a real, load-bearing limitation this
        // domain's own ordering (a fast, always-immediately-accepted Ack
        // racing ahead of a deliberately-delayed non-authoritative
        // capture's own catch-up) surfaces deterministically, every time,
        // not as a rare race. Tracked in TODO.md, not silently smoothed
        // over. AuthorityStatus itself still correctly reaches "accepted"
        // (checked above) -- only the Entity Store's own Data field is
        // affected.
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:ionmalert:alert-77g");
        Assert.IsTrue(row.LateArrivalFlag, "documents the real, deterministic late-arrival rejection described above");
        Assert.IsTrue(row.Data.Contains("tech-4"), "AckedBy was already folded, authoritatively, before the catch-up ran");
        Assert.IsFalse(row.Data.Contains("Urgent"), "Finding/Severity's own catch-up fold is skipped entirely once flagged late -- a real, open gap, not asserted as correct behavior");
    }

    public static async Task TheNeurologistSignsOffRejectedInsteadAndTheRecordNeverReachesTheAuthoritativeEntityStore(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowD.RegisterAsync(registry);
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var neuro = TestClaimsPrincipal.WithClaims(("sub", "neuro-12"), ("review", "ionm"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        var alert = (PublishResult.Accepted)await publish.PublishAsync("IonmAlertRaised",
            new PublishEventRequest(AppId, 1, """{ "AlertId": "alert-77h", "SubjectId": "S-0091", "Finding": "SSEP amplitude drop >50%, left lower extremity", "Severity": "Urgent" }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RunOneTickAsync(db, registry, publish);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{alert.CorrelationId}}", "decision": "rejected", "decidingActorId": "neuro-12", "reason": "artifact, not a true signal change" }""", null, null,
                Meaning: "reviewed"),
            neuro);
        await RunOneTickAsync(db, registry, publish);
        Assert.IsNotNull(decision);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == alert.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:ionmalert:alert-77h"));

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:ionmalert:alert-77h");
        Assert.IsTrue(liveRow.Data.Contains("Urgent"), "alert-77h remains visible in the Live View, re-labeled rejected, never deleted");
    }
}
