using System.Text.Json.Nodes;
using EventStore.Flows;
using Samples.Vitals;

namespace EventStore.UnitTests;

// ADR-101 -- the real, embedded .puml (docs/domains/clinical-trials-
// device-telemetry/features/adverse-event-capture-and-review.puml), read
// and parsed exactly as it will be at runtime, not a re-typed copy.
[TestClass]
public class VitalsWorkflowBFlowTests
{
    [TestMethod]
    public void TheRealEmbeddedPumlParsesAndBuildsAValidFlowDefinition()
    {
        var flow = VitalsWorkflowBFlow.Build();

        Assert.AreEqual("vitals-workflow-b-adverse-event-review", flow.Name);
        Assert.AreEqual("AdverseEventReported", flow.RaiserEventType);
        CollectionAssert.Contains(flow.CollectResolverEventTypes().ToArray(), "authorityDecision");
    }

    [TestMethod]
    public void AFreshlyCapturedAdverseEventAlwaysPausesAtThePiReviewTask()
    {
        var projection = new FlowProjection(VitalsWorkflowBFlow.Build());
        var mergedState = new JsonObject { ["AeId"] = "ae-1042", ["SubjectId"] = "S-0091", ["Severity"] = "Severe", ["SeriousAdverseEvent"] = true };

        var task = projection.Project("ae-1042-evt", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("PI must review and sign off on the adverse event", task!.Description);
        Assert.AreEqual("review:ae", task.RequiredClaim);
        Assert.AreEqual("trial1", task.AppId);
        Assert.AreEqual("ae-1042", task.EntityId);
    }

    [TestMethod]
    public void ANonSeriousAdverseEventStillPausesAtThePiReviewTask()
    {
        // Real finding (VitalsWorkflowB.Flow.cs's own comment): this flow
        // cannot see the real AuthorityStatus/attestedClaims.reviewPending
        // gate (not exposed via the HTTP Follow envelope), so it treats
        // every captured AE as needing review, matching the domain's own
        // feature doc Gherkin where both a serious and a non-serious AE
        // end up pending_review.
        var projection = new FlowProjection(VitalsWorkflowBFlow.Build());
        var mergedState = new JsonObject { ["AeId"] = "ae-1039", ["SeriousAdverseEvent"] = false };

        var task = projection.Project("ae-1039-evt", mergedState);

        Assert.IsNotNull(task);
    }

    [TestMethod]
    public void AnAcceptedDecisionResolvesTheTask()
    {
        var projection = new FlowProjection(VitalsWorkflowBFlow.Build());
        var mergedState = new JsonObject
        {
            ["AeId"] = "ae-1042",
            ["SeriousAdverseEvent"] = true,
            ["targetEventId"] = "ae-1042-evt",
            ["decision"] = "accepted",
        };

        Assert.IsNull(projection.Project("ae-1042-evt", mergedState));
    }

    [TestMethod]
    public void ARejectedDecisionAlsoResolvesTheTask()
    {
        var projection = new FlowProjection(VitalsWorkflowBFlow.Build());
        var mergedState = new JsonObject
        {
            ["AeId"] = "ae-1039",
            ["SeriousAdverseEvent"] = false,
            ["targetEventId"] = "ae-1039-evt",
            ["decision"] = "rejected",
        };

        Assert.IsNull(projection.Project("ae-1039-evt", mergedState));
    }
}
