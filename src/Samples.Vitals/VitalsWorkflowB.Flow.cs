using EventStore.Flows;

namespace Samples.Vitals;

// ADR-101 -- the real .puml at docs/domains/clinical-trials-device-
// telemetry/features/adverse-event-capture-and-review.puml, embedded as
// Samples.Vitals.adverse-event-capture-and-review.puml (see
// Samples.Vitals.csproj), narrating this workflow's already-built,
// already-tested behavior (VitalsWorkflowB.cs, unmodified by this file).
//
// Known, deliberate limitation, not a bug: the real ADR-042 gate for
// "does this AE need PI review" is AuthorityStatus/attestedClaims.
// reviewPending, set by the framework BEFORE storage -- confirmed by
// reading src/EventStore.Follow.Api/FollowEndpoints.cs directly, neither
// field is included in the SSE envelope ProjectionHost's FollowClient
// consumes (only eventId/sequenceNumber/occurredAt/parentEventIds/
// payload are). This flow therefore cannot see the real gate and treats
// EVERY captured AdverseEventReported as needing review -- a strict
// superset of the real gate (an AE the real system already
// auto-accepted still gets a PendingTask row here), never the other
// direction. Matches this domain's own feature doc Gherkin, where both
// worked scenarios (SeriousAdverseEvent true and false) end up
// pending_review and go through PI sign-off anyway.
public static class VitalsWorkflowBFlow
{
    public static FlowDefinition Build() => FlowDefinition.Parse(
        name: "vitals-workflow-b-adverse-event-review",
        raiserEventType: "AdverseEventReported",
        appId: VitalsWorkflowB.AppId,
        entityIdField: "$.AeId",
        pumlSource: EmbeddedPuml.Read(typeof(VitalsWorkflowBFlow).Assembly, "Samples.Vitals.adverse-event-capture-and-review.puml"),
        actions: FlowActions.NarrateAll("vitals-workflow-b",
            "Coordinator publishes AdverseEventReported",
            "AuthorityStatus set to pending_review (ADR-035/042)",
            "PI delegates scoped secondary opinion access (ADR-043)",
            "Colleague reviews via delegated read",
            "Entity Store catches up now (accepted, ADR-042)",
            "Entity Store never reflects this event (rejected, stays visible in the Live View, ADR-042)"));
}
