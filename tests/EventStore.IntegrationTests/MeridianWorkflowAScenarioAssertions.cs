using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Meridian;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Meridian proving-ground sample's Workflow A --
// Document/Biometric Capture -> Verification (docs/domains/digital-
// identity-kyc/features/document-and-biometric-capture.md +
// customer-onboarding-and-identity-verification.md). See
// MeridianWorkflowA.cs's own header comment for the real-vs-doc
// divergence on how self-attestation is modeled.
internal static class MeridianWorkflowAScenarioAssertions
{
    private const string AppId = MeridianWorkflowA.AppId;

    public static async Task UploadingAPassportScanAndLinkingItToTheApplicantBothGenerallyAndToThisEvent(
        SchemaRegistryService registry, PublishService publish, AttachmentService attachments, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);

        var upload = await attachments.UploadAsync(System.Text.Encoding.UTF8.GetBytes("passport-bytes-1001"), "image/jpeg", null, null, null, TestClaimsPrincipal.None);
        var contentHash = ((UploadAttachmentResult.Created)upload).ContentHash;

        var result = (PublishResult.Accepted)await publish.PublishAsync("IdentityDocumentUploaded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001", "DocumentType": "passport", "ExtractedDocumentNumber": "P-889231" }""",
                null, null, AttachmentContentHashes: [contentHash]),
            TestClaimsPrincipal.None);

        Assert.AreEqual("accepted", result.AuthorityStatus);
        var refs = await db.AttachmentRefs.AsNoTracking().Where(r => r.ContentHash == contentHash).ToListAsync();
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual(result.CorrelationId, refs[0].EventId, "the attachment is linked to the specific publishing event (ADR-032's two-step handoff)");
    }

    public static async Task AProofOfAddressLetterIsUploadedAndLinkedTheSameWayAsASecondDocumentType(
        SchemaRegistryService registry, PublishService publish, AttachmentService attachments)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var upload = await attachments.UploadAsync(System.Text.Encoding.UTF8.GetBytes("poa-bytes-1001"), "application/pdf", null, null, null, TestClaimsPrincipal.None);
        var contentHash = ((UploadAttachmentResult.Created)upload).ContentHash;

        var result = await publish.PublishAsync("IdentityDocumentUploaded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001", "DocumentType": "proof_of_address", "ExtractedDocumentNumber": "N/A" }""",
                null, null, AttachmentContentHashes: [contentHash]),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
    }

    public static async Task AConfidentLivenessResultIsCapturedAsAcceptedAndFoldsImmediately(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("BiometricCaptureRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001b", "CaptureType": "liveness_video", "LivenessCheckResult": "pass", "LivenessConfidence": 0.93 }""", null, null),
            TestClaimsPrincipal.None);
        Assert.AreEqual("accepted", result.AuthorityStatus);

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001b");
        Assert.IsTrue(row.Data.Contains("pass"));
    }

    public static async Task AnInconclusiveLivenessResultIsCapturedAsPendingReviewViaTheExplicitReviewPendingMarker(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("BiometricCaptureRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001c", "CaptureType": "liveness_video", "LivenessCheckResult": "inconclusive", "LivenessConfidence": 0.41 }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);
        Assert.AreEqual("pending_review", result.AuthorityStatus);

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001c");
        Assert.IsTrue(liveRow.Data.Contains("inconclusive"));
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001c"),
            "the authoritative Entity Store does not yet reflect this contribution -- it catches up only once a later authorityDecision accepts it (ADR-042)");
    }

    public static async Task AnAnalystsAuthorityDecisionResolvesAnInconclusiveLivenessCaptureAndTheAuthoritativeEntityStoreCatchesUp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var analyst = TestClaimsPrincipal.WithClaims(("sub", "analyst-1"), ("identity", "review"));

        var bio = (PublishResult.Accepted)await publish.PublishAsync("BiometricCaptureRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001d", "CaptureType": "liveness_video", "LivenessCheckResult": "inconclusive", "LivenessConfidence": 0.41 }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{bio.CorrelationId}}", "decision": "accepted", "decidingActorId": "analyst-1", "reason": "manual liveness review confirmed match" }""", null, null),
            analyst);
        Assert.IsNotNull(decision);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == bio.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus);
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001d");
        Assert.IsTrue(row.Data.Contains("inconclusive"), "reuses the exact same AuthorityDecisionResolver mechanism the identity-claim review below already uses -- not a second resolver");
    }

    public static async Task DocumentsAndBiometricResultAreBothVisibleToAnAnalystBeforeTheIdentityClaimIsEvenSubmitted(
        SchemaRegistryService registry, PublishService publish, AttachmentService attachments, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var passportUpload = await attachments.UploadAsync(System.Text.Encoding.UTF8.GetBytes("passport-bytes-1001e"), "image/jpeg", null, null, null, TestClaimsPrincipal.None);

        await publish.PublishAsync("IdentityDocumentUploaded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001e", "DocumentType": "passport", "ExtractedDocumentNumber": "P-1" }""", null, null,
                AttachmentContentHashes: [((UploadAttachmentResult.Created)passportUpload).ContentHash]),
            TestClaimsPrincipal.None);
        await publish.PublishAsync("BiometricCaptureRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001e", "CaptureType": "liveness_video", "LivenessCheckResult": "pass", "LivenessConfidence": 0.9 }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001e");
        Assert.IsTrue(row.Data.Contains("passport") && row.Data.Contains("pass"),
            "ChangeKind.Partial merge means this entity accumulates fields from multiple event types over time -- no IdentityClaimSubmitted event needs to exist yet");
    }

    public static async Task AnApplicantSelfAttestsAndTheClaimLandsUnattestedPersistedImmediately(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("IdentityClaimSubmitted",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001", "Did": "did:key:z6Mkf7...", "ClaimedLegalName": "Jane Smith", "DateOfBirth": "1990-03-01", "DocumentType": "passport" }""",
                null, null, AttestedActorId: "did:key:z6Mkf7..."),
            TestClaimsPrincipal.None);

        Assert.AreEqual("unattested", result.AuthorityStatus, "the DID proves applicant-1001 controls that key -- not that \"Jane Smith\"/the claimed DOB are real (ADR-036's own distinction)");
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == result.CorrelationId);
        Assert.IsFalse(string.IsNullOrEmpty(stored.ChainHash), "durably persisted immediately, never blocked on any identity-provider round trip (ADR-023)");
    }

    public static async Task AnAnalystLackingTheIdentityReviewClaimCannotPublishAnAuthorityDecision(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var clerk = TestClaimsPrincipal.WithClaims(("sub", "clerk-1"));

        var claim = (PublishResult.Accepted)await publish.PublishAsync("IdentityClaimSubmitted",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1002", "Did": "did:key:z6MkA2...", "ClaimedLegalName": "A Person", "DateOfBirth": "1985-01-01", "DocumentType": "passport" }""",
                null, null, AttestedActorId: "did:key:z6MkA2..."),
            TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{claim.CorrelationId}}", "decision": "accepted", "decidingActorId": "clerk-1" }""", null, null),
            clerk);

        Assert.IsInstanceOfType<PublishResult.Forbidden>(result);
        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == claim.CorrelationId);
        Assert.AreEqual("unattested", target.AuthorityStatus);
    }

    public static async Task AnAnalystHoldingIdentityReviewAcceptsTheClaimAndTheAuthoritativeEntityStoreNowFoldsIt(
        SchemaRegistryService registry, PublishService publish, AttachmentService attachments, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var analyst = TestClaimsPrincipal.WithClaims(("sub", "analyst-1"), ("identity", "review"));

        var upload = await attachments.UploadAsync(System.Text.Encoding.UTF8.GetBytes("passport-bytes-1001f"), "image/jpeg", null, null, null, TestClaimsPrincipal.None);
        await publish.PublishAsync("IdentityDocumentUploaded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001f", "DocumentType": "passport", "ExtractedDocumentNumber": "P-1" }""", null, null,
                AttachmentContentHashes: [((UploadAttachmentResult.Created)upload).ContentHash]),
            TestClaimsPrincipal.None);
        await publish.PublishAsync("BiometricCaptureRecorded",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001f", "CaptureType": "liveness_video", "LivenessCheckResult": "pass", "LivenessConfidence": 0.9 }""", null, null),
            TestClaimsPrincipal.None);
        var claim = (PublishResult.Accepted)await publish.PublishAsync("IdentityClaimSubmitted",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1001f", "Did": "did:key:z6Mkf7...", "ClaimedLegalName": "Jane Smith", "DateOfBirth": "1990-03-01", "DocumentType": "passport" }""",
                null, null, AttestedActorId: "did:key:z6Mkf7..."),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{claim.CorrelationId}}", "decision": "accepted", "decidingActorId": "analyst-1", "reason": "delegation chain and document scan verified" }""", null, null),
            analyst);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == claim.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus);
        Assert.AreEqual(decision.CorrelationId, target.AuthorityDecisionRef);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1001f");
        Assert.IsTrue(row.Version > 0, "documents/biometric capture/the claim itself each bump Version as they fold");
        Assert.IsTrue(row.Data.Contains("passport") && row.Data.Contains("pass") && row.Data.Contains("Jane Smith"),
            "documents + biometric + the identity claim itself all accumulate onto the SAME entity via ChangeKind.Partial");
    }

    public static async Task AnAnalystHoldingIdentityReviewRejectsTheClaimInsteadAndTheEntityStoreNeverReflectsIt(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await MeridianWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var analyst = TestClaimsPrincipal.WithClaims(("sub", "analyst-1"), ("identity", "review"));

        var claim = (PublishResult.Accepted)await publish.PublishAsync("IdentityClaimSubmitted",
            new PublishEventRequest(AppId, 1, """{ "ApplicantId": "applicant-1003", "Did": "did:key:z6MkQ9...", "ClaimedLegalName": "Someone Else", "DateOfBirth": "1970-01-01", "DocumentType": "passport" }""",
                null, null, AttestedActorId: "did:key:z6MkQ9..."),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{claim.CorrelationId}}", "decision": "rejected", "decidingActorId": "analyst-1", "reason": "document scan does not match claimed legal name" }""", null, null),
            analyst);
        Assert.IsNotNull(decision);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == claim.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1003"));

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:applicantidentity:applicant-1003");
        Assert.IsTrue(liveRow.Data.Contains("Someone Else"), "still visible in the Event Log/Live View, re-labeled rejected, never deleted");
    }
}
