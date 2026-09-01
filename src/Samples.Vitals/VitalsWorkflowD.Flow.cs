using EventStore.Flows;

namespace Samples.Vitals;

// ADR-101 -- the real .puml at docs/domains/clinical-trials-device-
// telemetry/features/intraoperative-monitoring-and-alert-response.puml,
// embedded as Samples.Vitals.intraoperative-monitoring-and-alert-response.puml
// (see Samples.Vitals.csproj), narrating this workflow's already-built,
// already-tested behavior (VitalsWorkflowD.cs, unmodified by this file).
//
// The technician's IonmAlertAcknowledged and the neurologist's later
// authorityDecision are independent facts (the feature doc's own explicit
// framing: "being acknowledged in real time never by itself moves
// AuthorityStatus") -- this flow narrates the acknowledgment as a plain
// action, never a task or a gate on the neurologist's own review task,
// matching that independence exactly. The ExpectedResponseWatcher's own
// escalation-on-timeout mechanism (ExpectedResponseMissing) is a separate,
// already-tested, fully automatic subsystem this flow doesn't re-narrate
// or gate on -- it isn't a human decision point.
public static class VitalsWorkflowDFlow
{
    public static FlowDefinition Build() => FlowDefinition.Parse(
        name: "vitals-workflow-d-ionm-alert-response",
        raiserEventType: "IonmAlertRaised",
        appId: VitalsWorkflowD.AppId,
        entityIdField: "$.AlertId",
        pumlSource: EmbeddedPuml.Read(typeof(VitalsWorkflowDFlow).Assembly, "Samples.Vitals.intraoperative-monitoring-and-alert-response.puml"),
        actions: FlowActions.NarrateAll("vitals-workflow-d",
            "Detector publishes IonmAlertRaised (TelemetryPointer to fast channel)",
            "ExpectedResponseTracker starts (2-minute acknowledgment window, ADR-094)",
            "Technician acknowledges within the window (independent of AuthorityStatus)",
            "Entity Store catches up now (accepted, ADR-042), reflecting Finding, Severity, and AckedBy together",
            "Entity Store never reflects this event (rejected, stays visible in the Live View, ADR-042)"));
}
