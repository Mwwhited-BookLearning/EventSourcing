using EventStore.Flows;

namespace Samples.Meridian;

// ADR-101 -- the real .puml at docs/domains/digital-identity-kyc/features/
// periodic-screening-and-sar-escalation.puml, embedded as
// Samples.Meridian.periodic-screening-and-sar-escalation.puml (see
// Samples.Meridian.csproj), narrating this workflow's already-built,
// already-tested behavior (MeridianWorkflowC.cs, unmodified by this file).
//
// Two SEQUENTIAL task nodes, not two alternative resolutions of one task --
// confirmed against the real Gherkin: SarFilingRecorded only ever happens
// AFTER an authorityDecision already accepted the match ("Given a
// SanctionsScreeningPerformed event ... was confirmed accepted, per
// above"), never as an alternative way to resolve the review itself. The
// existing engine needed no changes for this: both authorityDecision
// (correlatedBy default "targetEventId") and SarFilingRecorded
// (correlatedBy "TargetScreeningEventId") independently route to the SAME
// key -- the screening event's own EventId -- so their fields merge into
// one snapshot the interpreter walks straight through, pausing at
// whichever of the two tasks is still open.
public static class MeridianWorkflowCFlow
{
    public static FlowDefinition Build() => FlowDefinition.Parse(
        name: "meridian-workflow-c-sanctions-screening-and-sar",
        raiserEventType: "SanctionsScreeningPerformed",
        appId: MeridianWorkflowC.AppId,
        entityIdField: "$.ApplicantId",
        pumlSource: EmbeddedPuml.Read(typeof(MeridianWorkflowCFlow).Assembly, "Samples.Meridian.periodic-screening-and-sar-escalation.puml"),
        actions: FlowActions.NarrateAll("meridian-workflow-c",
            "PeriodicScreeningWorker publishes SanctionsScreeningPerformed",
            "Entity Store catches up now, confirmed match (ADR-042)",
            "SAR filed, step-up-signed (ADR-066)",
            "Entity Store never reflects this event (false positive, ADR-042)",
            "Entity Store folds immediately (routine, no match, ADR-042)"));
}
