using System.Text.Json.Nodes;
using EventStore.Flows;
using Samples.Meridian;

namespace EventStore.UnitTests;

// ADR-101 -- the real, embedded .puml (docs/domains/digital-identity-kyc/
// features/periodic-screening-and-sar-escalation.puml). The one flow with
// TWO sequential task nodes (confirm-the-match, then file-the-SAR) --
// exercises that the engine needed no changes to support this: both
// authorityDecision and SarFilingRecorded route to the same key via their
// own distinct correlatedBy fields, so their fields merge into one
// snapshot the interpreter walks straight through.
[TestClass]
public class MeridianWorkflowCFlowTests
{
    [TestMethod]
    public void TheRealEmbeddedPumlParsesAndBuildsAValidFlowDefinitionWithBothResolverTypes()
    {
        var flow = MeridianWorkflowCFlow.Build();

        Assert.AreEqual("meridian-workflow-c-sanctions-screening-and-sar", flow.Name);
        Assert.AreEqual("SanctionsScreeningPerformed", flow.RaiserEventType);
        var resolverTypes = flow.CollectResolverEventTypes().ToArray();
        CollectionAssert.Contains(resolverTypes, "authorityDecision");
        CollectionAssert.Contains(resolverTypes, "SarFilingRecorded");
    }

    [TestMethod]
    public void ARoutineScreeningWithNoMatchNeverPausesAtAnyTask()
    {
        var projection = new FlowProjection(MeridianWorkflowCFlow.Build());
        var mergedState = new JsonObject { ["ApplicantId"] = "applicant-1001", ["MatchFound"] = false };

        Assert.IsNull(projection.Project("screen-0", mergedState));
    }

    [TestMethod]
    public void AMatchPausesAtTheConfirmOrDismissTask()
    {
        var projection = new FlowProjection(MeridianWorkflowCFlow.Build());
        var mergedState = new JsonObject { ["ApplicantId"] = "applicant-1001", ["MatchFound"] = true, ["MatchConfidence"] = 0.87 };

        var task = projection.Project("screen-1", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("Compliance officer must confirm or dismiss the sanctions match", task!.Description);
        Assert.AreEqual("identity:aml-review", task.RequiredClaim);
    }

    [TestMethod]
    public void DismissingTheMatchAsAFalsePositiveResolvesEverythingNoSarTask()
    {
        var projection = new FlowProjection(MeridianWorkflowCFlow.Build());
        var mergedState = new JsonObject
        {
            ["ApplicantId"] = "applicant-1001",
            ["MatchFound"] = true,
            ["targetEventId"] = "screen-2",
            ["decision"] = "rejected",
        };

        Assert.IsNull(projection.Project("screen-2", mergedState));
    }

    [TestMethod]
    public void ConfirmingTheMatchAdvancesToTheFileSarTask()
    {
        var projection = new FlowProjection(MeridianWorkflowCFlow.Build());
        var mergedState = new JsonObject
        {
            ["ApplicantId"] = "applicant-1001",
            ["MatchFound"] = true,
            ["targetEventId"] = "screen-1",
            ["decision"] = "accepted",
        };

        var task = projection.Project("screen-1", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("Compliance officer must file a SAR for the confirmed match", task!.Description);
        Assert.AreEqual("identity:aml-review", task.RequiredClaim);
    }

    [TestMethod]
    public void FilingTheSarFinallyResolvesEverything()
    {
        var projection = new FlowProjection(MeridianWorkflowCFlow.Build());
        var mergedState = new JsonObject
        {
            ["ApplicantId"] = "applicant-1001",
            ["MatchFound"] = true,
            ["targetEventId"] = "screen-1",
            ["decision"] = "accepted",
            ["TargetScreeningEventId"] = "screen-1",
            ["FilingReferenceId"] = "SAR-2026-00417",
        };

        Assert.IsNull(projection.Project("screen-1", mergedState));
    }
}
