using System.Text.Json.Nodes;
using EventStore.Flows;
using Samples.Meridian;

namespace EventStore.UnitTests;

// ADR-101 -- the real, embedded .puml (docs/domains/digital-identity-kyc/
// features/customer-onboarding-and-identity-verification.puml).
[TestClass]
public class MeridianWorkflowAFlowTests
{
    [TestMethod]
    public void TheRealEmbeddedPumlParsesAndBuildsAValidFlowDefinition()
    {
        var flow = MeridianWorkflowAFlow.Build();

        Assert.AreEqual("meridian-workflow-a-identity-verification", flow.Name);
        Assert.AreEqual("IdentityClaimSubmitted", flow.RaiserEventType);
        CollectionAssert.Contains(flow.CollectResolverEventTypes().ToArray(), "authorityDecision");
    }

    [TestMethod]
    public void AFreshlySubmittedClaimPausesAtTheAnalystReviewTask()
    {
        var projection = new FlowProjection(MeridianWorkflowAFlow.Build());
        var mergedState = new JsonObject { ["ApplicantId"] = "applicant-1001", ["Did"] = "did:key:z6Mkf7..." };

        var task = projection.Project("claim-1001", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("Analyst must review the self-attested identity claim", task!.Description);
        Assert.AreEqual("identity:review", task.RequiredClaim);
        Assert.AreEqual("kyc", task.AppId);
        Assert.AreEqual("applicant-1001", task.EntityId);
    }

    [TestMethod]
    public void AnAcceptedDecisionResolvesTheTask()
    {
        var projection = new FlowProjection(MeridianWorkflowAFlow.Build());
        var mergedState = new JsonObject
        {
            ["ApplicantId"] = "applicant-1001",
            ["targetEventId"] = "claim-1001",
            ["decision"] = "accepted",
        };

        Assert.IsNull(projection.Project("claim-1001", mergedState));
    }

    [TestMethod]
    public void ARejectedDecisionAlsoResolvesTheTask()
    {
        var projection = new FlowProjection(MeridianWorkflowAFlow.Build());
        var mergedState = new JsonObject
        {
            ["ApplicantId"] = "applicant-1002",
            ["targetEventId"] = "claim-1002",
            ["decision"] = "rejected",
        };

        Assert.IsNull(projection.Project("claim-1002", mergedState));
    }
}
