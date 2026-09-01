using System.Text.Json.Nodes;
using EventStore.Flows;
using Samples.Vitals;

namespace EventStore.UnitTests;

// ADR-101 -- the real, embedded .puml (docs/domains/clinical-trials-
// device-telemetry/features/intraoperative-monitoring-and-alert-response.puml).
[TestClass]
public class VitalsWorkflowDFlowTests
{
    [TestMethod]
    public void TheRealEmbeddedPumlParsesAndBuildsAValidFlowDefinition()
    {
        var flow = VitalsWorkflowDFlow.Build();

        Assert.AreEqual("vitals-workflow-d-ionm-alert-response", flow.Name);
        Assert.AreEqual("IonmAlertRaised", flow.RaiserEventType);
        CollectionAssert.Contains(flow.CollectResolverEventTypes().ToArray(), "authorityDecision");
    }

    [TestMethod]
    public void AFreshlyRaisedAlertPausesAtTheNeurologistReviewTaskRegardlessOfAcknowledgment()
    {
        var projection = new FlowProjection(VitalsWorkflowDFlow.Build());
        var mergedState = new JsonObject { ["AlertId"] = "alert-77", ["SubjectId"] = "S-0091", ["Severity"] = "Urgent" };

        var task = projection.Project("alert-77-evt", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("Neurologist must review and sign off on the IONM alert", task!.Description);
        Assert.AreEqual("review:ionm", task.RequiredClaim);
        Assert.AreEqual("trial1", task.AppId);
        Assert.AreEqual("alert-77", task.EntityId);
    }

    [TestMethod]
    public void AnAcceptedDecisionResolvesTheTask()
    {
        var projection = new FlowProjection(VitalsWorkflowDFlow.Build());
        var mergedState = new JsonObject
        {
            ["AlertId"] = "alert-77",
            ["targetEventId"] = "alert-77-evt",
            ["decision"] = "accepted",
        };

        Assert.IsNull(projection.Project("alert-77-evt", mergedState));
    }

    [TestMethod]
    public void ARejectedDecisionAlsoResolvesTheTask()
    {
        var projection = new FlowProjection(VitalsWorkflowDFlow.Build());
        var mergedState = new JsonObject
        {
            ["AlertId"] = "alert-77",
            ["targetEventId"] = "alert-77-evt",
            ["decision"] = "rejected",
        };

        Assert.IsNull(projection.Project("alert-77-evt", mergedState));
    }
}
