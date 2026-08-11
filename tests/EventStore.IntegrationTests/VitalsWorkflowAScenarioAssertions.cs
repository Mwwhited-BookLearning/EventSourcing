using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Vitals;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Vitals proving-ground sample's Workflow A --
// Enrollment & Consent (docs/domains/clinical-trials-device-telemetry/
// features/patient-enrollment-and-informed-consent.md), mirroring that
// doc's own Gherkin scenarios. Exercises Samples.Vitals' real
// registration code (VitalsWorkflowA.RegisterAsync) against the real
// PublishService/RouterWorker/SchemaRegistryService pipeline -- the same
// "exercise the mechanics directly" pattern every other
// *ScenarioAssertions.cs file in this repo already establishes, proving
// the framework (not just the feature doc's own narrative) actually
// carries this domain's real workflow end to end.
internal static class VitalsWorkflowAScenarioAssertions
{
    private const string AppId = VitalsWorkflowA.AppId;

    public static async Task ACoordinatorScreensANewPatientAndTheRecordIsAcceptedImmediately(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();

        var result = (PublishResult.Accepted)await publish.PublishAsync(
            "PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091", "SiteId": "04-221", "ProtocolId": "trial1-proto-A", "ScreeningDate": "2026-07-20", "EligibilityStatus": "Eligible" }""", null, null),
            TestClaimsPrincipal.WithClaims(("patient", "enroll")));

        Assert.AreEqual("accepted", result.AuthorityStatus, "ordinary authenticated capture defaults to accepted (ADR-042)");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:patient:S-0091");
        Assert.IsTrue(row.Data.Contains("04-221") && row.Data.Contains("Eligible"));
    }

    public static async Task ACoordinatorCapturesInformedConsentWhichStartsNonAuthoritativePendingInvestigatorCountersignature(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var coordinator = TestClaimsPrincipal.WithClaims(("patient", "enroll"), ("consent", "capture"));

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091b", "SiteId": "04-221", "ProtocolId": "trial1-proto-A", "ScreeningDate": "2026-07-20", "EligibilityStatus": "Eligible" }""", null, null),
            coordinator);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var consent = (PublishResult.Accepted)await publish.PublishAsync("InformedConsentCaptured",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091b", "ConsentVersion": "v3", "ConsentObtainedAt": "2026-07-22T09:10:00Z", "WitnessActorId": "coord-3" }""", null, null,
                ReviewPending: true),
            coordinator);
        Assert.AreEqual("pending_review", consent.AuthorityStatus);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:patient:S-0091b");
        Assert.IsTrue(liveRow.Data.Contains("v3"), "the Live View folds every event immediately, no AuthorityStatus gate (ADR-042)");

        var authoritativeRow = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:patient:S-0091b");
        Assert.IsFalse(authoritativeRow.Data.Contains("v3"), "the authoritative Entity Store must not reflect ConsentVersion until the investigator countersigns");
    }

    public static async Task ACoordinatorCannotApproveTheirOwnConsentCapture(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var coordinator = TestClaimsPrincipal.WithClaims(("patient", "enroll"), ("consent", "capture"));

        var consent = (PublishResult.Accepted)await publish.PublishAsync("InformedConsentCaptured",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091c", "ConsentVersion": "v3", "ConsentObtainedAt": "2026-07-22T09:10:00Z", "WitnessActorId": "coord-3" }""", null, null,
                ReviewPending: true),
            coordinator);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{consent.CorrelationId}}", "decision": "accepted", "decidingActorId": "coord-3" }""", null, null),
            coordinator);

        Assert.IsInstanceOfType<PublishResult.Forbidden>(result, "\"SiteCoordinator\" bundles patient:enroll + consent:capture only (ADR-046) -- consent:approve belongs to \"PrincipalInvestigator\"");
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "authoritydecision"),
            "no authorityDecision event may be persisted for a rejected claims check");
    }

    public static async Task ThePIsCountersignatureWithoutSufficientStepUpAuthenticationIsChallengedNotStored(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var coordinator = TestClaimsPrincipal.WithClaims(("patient", "enroll"), ("consent", "capture"));
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"));

        var consent = (PublishResult.Accepted)await publish.PublishAsync("InformedConsentCaptured",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091d", "ConsentVersion": "v3", "ConsentObtainedAt": "2026-07-22T09:10:00Z", "WitnessActorId": "coord-3" }""", null, null,
                ReviewPending: true),
            coordinator);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{consent.CorrelationId}}", "decision": "accepted", "decidingActorId": "pi-7" }""", null, null,
                Meaning: "consent-approved"),
            pi);

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        CollectionAssert.AreEqual(new[] { "urn:trial:step-up" }, stepUp.AcrValues.ToArray());
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "authoritydecision"));
    }

    public static async Task ThePICountersignsApprovedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var coordinator = TestClaimsPrincipal.WithClaims(("patient", "enroll"), ("consent", "capture"));
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091", "SiteId": "04-221", "ProtocolId": "trial1-proto-A", "ScreeningDate": "2026-07-20", "EligibilityStatus": "Eligible" }""", null, null),
            coordinator);
        var consent = (PublishResult.Accepted)await publish.PublishAsync("InformedConsentCaptured",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091", "ConsentVersion": "v3", "ConsentObtainedAt": "2026-07-22T09:10:00Z", "WitnessActorId": "coord-3" }""", null, null,
                ReviewPending: true),
            coordinator);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var approval = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{consent.CorrelationId}}", "decision": "accepted", "decidingActorId": "pi-7" }""", null, null,
                Meaning: "consent-approved"),
            pi);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedApproval = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == approval.CorrelationId);
        Assert.IsNotNull(storedApproval.Signature);
        Assert.AreEqual("pi-7", storedApproval.Signature!.SignerId);
        Assert.AreEqual("consent-approved", storedApproval.Signature.Meaning);
        Assert.AreEqual("urn:trial:step-up", storedApproval.Signature.Acr);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == consent.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus, "the same 'apply once, on the triggering condition' catch-up shape ADR-042's AuthorityDecisionResolver already establishes");

        var authoritativeRow = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:patient:S-0091");
        Assert.IsTrue(authoritativeRow.Data.Contains("v3"), "the authoritative Entity Store must catch up to reflect the now-accepted consent fields");
    }

    public static async Task ThePIRejectsTheConsentCaptureAndEnrollmentStaysPendingUntilItsRecaptured(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var coordinator = TestClaimsPrincipal.WithClaims(("patient", "enroll"), ("consent", "capture"));
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0044", "SiteId": "04-221", "ProtocolId": "trial1-proto-A", "ScreeningDate": "2026-07-20", "EligibilityStatus": "Eligible" }""", null, null),
            coordinator);
        var consent = (PublishResult.Accepted)await publish.PublishAsync("InformedConsentCaptured",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0044", "ConsentVersion": "v3", "ConsentObtainedAt": "2026-07-22T09:10:00Z", "WitnessActorId": "coord-3" }""", null, null,
                ReviewPending: true),
            coordinator);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var rejection = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{consent.CorrelationId}}", "decision": "rejected", "decidingActorId": "pi-7", "reason": "witness signature illegible" }""", null, null,
                Meaning: "consent-rejected"),
            pi);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);
        Assert.IsNotNull(rejection);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == consent.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);

        var authoritativeRow = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:patient:S-0044");
        Assert.IsFalse(authoritativeRow.Data.Contains("v3"), "a rejected consent must never fold into the authoritative Entity Store -- the coordinator must recapture consent (a new InformedConsentCaptured event) before resubmitting");
    }
}
