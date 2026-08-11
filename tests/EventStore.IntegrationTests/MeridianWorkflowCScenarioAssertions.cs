using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Meridian;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Meridian proving-ground sample's Workflow C --
// Periodic Screening & SAR Escalation (docs/domains/digital-identity-kyc/
// features/periodic-screening-and-sar-escalation.md). Unlike every other
// workflow built this session, no real-vs-doc divergence was found here
// -- confirmed by actually running every scenario, not assumed from the
// doc's own claim that "no new framework mechanism is introduced."
internal static class MeridianWorkflowCScenarioAssertions
{
    private const string AppId = MeridianWorkflowC.AppId;

    public static async Task ARoutinePeriodicScreeningWithNoMatchIsAcceptedAndFoldsImmediately(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("SanctionsScreeningPerformed",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001", "ScreeningDate": "2026-07-30", "ListsChecked": ["OFAC-SDN"], "MatchFound": false }""", null, null),
            TestClaimsPrincipal.None);
        Assert.AreEqual("accepted", result.AuthorityStatus);

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001");
        Assert.IsTrue(row.Data.Contains("OFAC-SDN"));
    }

    public static async Task ASanctionsListMatchIsAlwaysCapturedAsPendingReviewRegardlessOfConfidence(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("SanctionsScreeningPerformed",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001b", "ScreeningDate": "2026-07-30", "ListsChecked": ["OFAC-SDN"], "MatchFound": true, "MatchConfidence": 0.87, "MatchedName": "Jane Smith", "MatchedListEntryId": "SDN-44291" }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);
        Assert.AreEqual("pending_review", result.AuthorityStatus, "ADR-042's automated-detector trigger applied to a sanctions hit -- never auto-accepted, unlike an ordinary publish's default");

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001b");
        Assert.IsTrue(liveRow.Data.Contains("true"));
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001b"));
    }

    public static async Task AUserHoldingNeitherIdentityReviewNorIdentityAmlReviewCannotDecideAFlaggedMatch(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);

        var screen = (PublishResult.Accepted)await publish.PublishAsync("SanctionsScreeningPerformed",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001c", "ScreeningDate": "2026-07-30", "ListsChecked": ["OFAC-SDN"], "MatchFound": true, "MatchConfidence": 0.6, "MatchedName": "A Person", "MatchedListEntryId": "SDN-1" }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{screen.CorrelationId}}", "decision": "accepted", "decidingActorId": "clerk-1" }""", null, null),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Forbidden>(result);
        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == screen.CorrelationId);
        Assert.AreEqual("pending_review", target.AuthorityStatus);
    }

    public static async Task AComplianceOfficerHoldingIdentityAmlReviewConfirmsTheHitAndTheEntityStoreCatchesUp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var officer = TestClaimsPrincipal.WithClaims(("sub", "compliance-officer-1"), ("identity", "aml-review"));

        var screen = (PublishResult.Accepted)await publish.PublishAsync("SanctionsScreeningPerformed",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001d", "ScreeningDate": "2026-07-30", "ListsChecked": ["OFAC-SDN"], "MatchFound": true, "MatchConfidence": 0.87, "MatchedName": "Jane Smith", "MatchedListEntryId": "SDN-44291" }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{screen.CorrelationId}}", "decision": "accepted", "decidingActorId": "compliance-officer-1", "reason": "confirmed match against SDN-44291" }""", null, null),
            officer);
        Assert.IsNotNull(decision);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == screen.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus, "identity:aml-review satisfies authorityDecision's RequiredClaims OR-set (ADR-050) -- the same generic event type onboarding's analyst review uses");
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001d");
        Assert.IsTrue(row.Data.Contains("SDN-44291"));
    }

    public static async Task AComplianceOfficerClearsAFlaggedMatchAsAFalsePositiveAndNoSarIsFiled(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var officer = TestClaimsPrincipal.WithClaims(("sub", "compliance-officer-1"), ("identity", "aml-review"));

        var screen = (PublishResult.Accepted)await publish.PublishAsync("SanctionsScreeningPerformed",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001e", "ScreeningDate": "2026-07-30", "ListsChecked": ["OFAC-SDN"], "MatchFound": true, "MatchConfidence": 0.52, "MatchedName": "Different Person", "MatchedListEntryId": "SDN-2" }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{screen.CorrelationId}}", "decision": "rejected", "decidingActorId": "compliance-officer-1", "reason": "different date of birth than SDN-44291 subject" }""", null, null),
            officer);
        Assert.IsNotNull(decision);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == screen.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsTrue(target.Payload.Contains("Different Person"), "RejectionBehavior Annotate (default) -- Payload is untouched");
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "sarfilingrecorded"));
    }

    public static async Task FilingASarWithoutSufficientStepUpFailsWithAnRfc9470Challenge(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);
        var officer = TestClaimsPrincipal.WithClaims(("sub", "compliance-officer-1"), ("identity", "aml-review"));

        var result = await publish.PublishAsync("SarFilingRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001f", "TargetScreeningEventId": "00000000-0000-0000-0000-000000000000", "FilingReferenceId": "SAR-2026-00417", "Narrative": "confirmed OFAC-SDN match, filed per BSA requirements" }""", null, null,
                Meaning: "approved filing"),
            officer);

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        CollectionAssert.AreEqual(new[] { "urn:kyc:acr:step-up" }, stepUp.AcrValues.ToArray());
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "sarfilingrecorded"));
    }

    public static async Task AfterSteppingUpTheRetriedSarFilingSucceedsAndCapturesASignature(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowC.RegisterAsync(registry);
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var officer = TestClaimsPrincipal.WithClaims(("sub", "compliance-officer-1"), ("identity", "aml-review"), ("acr", "urn:kyc:acr:step-up"), ("auth_time", recentAuthTime));

        var result = (PublishResult.Accepted)await publish.PublishAsync("SarFilingRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001g", "TargetScreeningEventId": "00000000-0000-0000-0000-000000000000", "FilingReferenceId": "SAR-2026-00417", "Narrative": "confirmed OFAC-SDN match, filed per BSA requirements" }""", null, null,
                Meaning: "approved filing"),
            officer);

        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.IsNotNull(stored.Signature);
        Assert.AreEqual("compliance-officer-1", stored.Signature!.SignerId);
        Assert.AreEqual("approved filing", stored.Signature.Meaning);
        Assert.AreEqual("urn:kyc:acr:step-up", stored.Signature.Acr);
        // The actual FinCEN BSA E-Filing submission is explicitly out of
        // scope (ADR-072's IInterchangeFormatAdapter seam, not built here).
    }
}
