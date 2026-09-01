using EventStore.Flows;

namespace Samples.Meridian;

// ADR-101 -- the real .puml at docs/domains/digital-identity-kyc/features/
// customer-onboarding-and-identity-verification.puml, embedded as
// Samples.Meridian.customer-onboarding-and-identity-verification.puml (see
// Samples.Meridian.csproj), narrating this workflow's already-built,
// already-tested behavior (MeridianWorkflowA.cs, unmodified by this file).
//
// Raiser event is IdentityClaimSubmitted specifically, not
// IdentityDocumentUploaded/BiometricCaptureRecorded -- those two are
// narrated as a plain preceding action (they're real, already-registered
// event types, but this domain's own feature doc Gherkin centers its
// Background/scenarios on IdentityClaimSubmitted as the record the
// analyst actually reviews). Matches MeridianWorkflowA.cs's own real,
// documented divergence from that doc's original sequence diagram: no
// Router-driven Token-Exchange-triggered AuthorityStatus upgrade exists in
// EventStore.Router/EventStore.Inbox, so this flow narrates the real,
// built "unattested -> analyst decision -> accepted" shape instead.
public static class MeridianWorkflowAFlow
{
    public static FlowDefinition Build() => FlowDefinition.Parse(
        name: "meridian-workflow-a-identity-verification",
        raiserEventType: "IdentityClaimSubmitted",
        appId: MeridianWorkflowA.AppId,
        entityIdField: "$.ApplicantId",
        pumlSource: EmbeddedPuml.Read(typeof(MeridianWorkflowAFlow).Assembly, "Samples.Meridian.customer-onboarding-and-identity-verification.puml"),
        actions: FlowActions.NarrateAll("meridian-workflow-a",
            "Applicant uploads identity document and biometric capture",
            "Applicant self-attests IdentityClaimSubmitted via UCAN (AuthorityStatus starts unattested, ADR-036)",
            "Router exchanges the UCAN via token exchange (AuthorityStatus becomes pending_review on success)",
            "Entity Store folds the claim now (accepted, ADR-042)",
            "Entity Store never reflects this claim (rejected, stays visible in the Live View, ADR-042)"));
}
