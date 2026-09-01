using System.Text.Json.Nodes;
using EventStore.Flows;

namespace EventStore.UnitTests;

// ADR-101. Direct coverage for the promoted parser (unchanged from
// spikes/user-flow-dsl/PlantUmlNativeSpike/) plus the new, generalized
// FlowInterpreter/TaskDeclaration/FlowProjection behavior the promotion
// added: payload-aware actions/conditions, the "task" label-text
// convention, and the stateless pause-at-unresolved-task walk.
[TestClass]
public class FlowInterpreterTests
{
    [TestMethod]
    public void ParserProducesActionIfAndStopNodesFromARealPumlSubset()
    {
        const string puml = """
            @startuml
            start
            :Coordinator publishes AdverseEventReported;
            if (SeriousAdverseEvent?) then (yes)
              :AuthorityStatus = pending_review;
            else (no)
              :Fold immediately (Full);
            endif
            stop
            @enduml
            """;

        var ast = PlantUmlActivityParser.Parse(puml);

        Assert.AreEqual(3, ast.Count);
        Assert.IsInstanceOfType<ActionNode>(ast[0]);
        Assert.IsInstanceOfType<IfNode>(ast[1]);
        Assert.IsInstanceOfType<StopNode>(ast[2]);
    }

    [TestMethod]
    public void ParserThrowsOnAnUnsupportedLineRatherThanSilentlyIgnoringIt()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            PlantUmlActivityParser.Parse("@startuml\npartition Foo {\n@enduml"));
    }

    [TestMethod]
    public void TaskDeclarationParsesDescriptionClaimAndSingleResolvedByType()
    {
        var parsed = TaskDeclaration.TryParse(
            """task "PI must review the adverse event" claim="review:ae" resolvedBy="authorityDecision" """.TrimEnd(),
            out var task);

        Assert.IsTrue(parsed);
        Assert.AreEqual("PI must review the adverse event", task!.Description);
        Assert.AreEqual("review:ae", task.RequiredClaim);
        CollectionAssert.AreEqual(new[] { "authorityDecision" }, task.ResolvedByEventTypes.ToArray());
        Assert.AreEqual("targetEventId", task.CorrelatedBy); // default
    }

    [TestMethod]
    public void TaskDeclarationParsesOrOfListResolvedByAndAnExplicitCorrelatedBy()
    {
        var parsed = TaskDeclaration.TryParse(
            """task "Dismiss or file" claim="identity:aml-review" resolvedBy="SarFilingRecorded|authorityDecision" correlatedBy="TargetScreeningEventId" """.TrimEnd(),
            out var task);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new[] { "SarFilingRecorded", "authorityDecision" }, task!.ResolvedByEventTypes.ToArray());
        Assert.AreEqual("TargetScreeningEventId", task.CorrelatedBy);
    }

    [TestMethod]
    public void TaskDeclarationDoesNotMatchAnOrdinaryActionLabel()
    {
        Assert.IsFalse(TaskDeclaration.TryParse("Coordinator publishes AdverseEventReported", out _));
    }

    [TestMethod]
    public void UnregisteredPlainActionThrowsRatherThanSilentlyNoOp()
    {
        var ast = PlantUmlActivityParser.Parse("@startuml\n:Some unregistered step;\n@enduml");
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>>(),
            new Dictionary<string, Func<JsonObject, bool>>());

        Assert.ThrowsExactly<InvalidOperationException>(() => interpreter.Evaluate(ast, new JsonObject()));
    }

    [TestMethod]
    public void RegisteredActionIsInvokedWithTheMergedState()
    {
        JsonObject? seen = null;
        var ast = PlantUmlActivityParser.Parse("@startuml\n:Narrate;\n@enduml");
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>> { ["Narrate"] = state => seen = state },
            new Dictionary<string, Func<JsonObject, bool>>());
        var mergedState = new JsonObject { ["SeverityScore"] = 8 };

        var outcome = interpreter.Evaluate(ast, mergedState);

        Assert.IsInstanceOfType<FlowCompleted>(outcome);
        Assert.AreSame(mergedState, seen);
    }

    [TestMethod]
    public void FieldTruthyConditionTakesTheYesBranchWhenTheBooleanFieldIsTrue()
    {
        var ast = PlantUmlActivityParser.Parse("""
            @startuml
            if (SeriousAdverseEvent?) then (yes)
              :Yes branch;
            else (no)
              :No branch;
            endif
            @enduml
            """);
        var taken = new List<string>();
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>>
            {
                ["Yes branch"] = _ => taken.Add("yes"),
                ["No branch"] = _ => taken.Add("no"),
            },
            new Dictionary<string, Func<JsonObject, bool>>());

        interpreter.Evaluate(ast, new JsonObject { ["SeriousAdverseEvent"] = true });

        CollectionAssert.AreEqual(new[] { "yes" }, taken);
    }

    [TestMethod]
    public void FieldTruthyConditionTreatsAnAcceptedStringDecisionOutcomeAsTrue()
    {
        var ast = PlantUmlActivityParser.Parse("""
            @startuml
            if (decision?) then (yes)
              :Yes branch;
            else (no)
              :No branch;
            endif
            @enduml
            """);
        var taken = new List<string>();
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>>
            {
                ["Yes branch"] = _ => taken.Add("yes"),
                ["No branch"] = _ => taken.Add("no"),
            },
            new Dictionary<string, Func<JsonObject, bool>>());

        interpreter.Evaluate(ast, new JsonObject { ["decision"] = "accepted" });

        CollectionAssert.AreEqual(new[] { "yes" }, taken);
    }

    [TestMethod]
    public void UnregisteredNonFieldConditionThrows()
    {
        var ast = PlantUmlActivityParser.Parse("@startuml\nif (some custom check?!) then (yes)\n:X;\nelse (no)\n:Y;\nendif\n@enduml");
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>> { ["X"] = _ => { }, ["Y"] = _ => { } },
            new Dictionary<string, Func<JsonObject, bool>>());

        Assert.ThrowsExactly<InvalidOperationException>(() => interpreter.Evaluate(ast, new JsonObject()));
    }

    [TestMethod]
    public void WalkPausesAtAnUnresolvedTaskAndDoesNotRunLaterActions()
    {
        var ast = PlantUmlActivityParser.Parse("""
            @startuml
            :Before;
            :task "PI must review" claim="review:ae" resolvedBy="authorityDecision";
            :After;
            @enduml
            """);
        var ran = new List<string>();
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>>
            {
                ["Before"] = _ => ran.Add("before"),
                ["After"] = _ => ran.Add("after"),
            },
            new Dictionary<string, Func<JsonObject, bool>>());

        var outcome = interpreter.Evaluate(ast, new JsonObject());

        Assert.IsInstanceOfType<FlowPausedAtTask>(outcome);
        Assert.AreEqual("PI must review", ((FlowPausedAtTask)outcome).Task.Description);
        CollectionAssert.AreEqual(new[] { "before" }, ran); // "after" never ran
    }

    [TestMethod]
    public void WalkContinuesPastAnAlreadyResolvedTask()
    {
        var ast = PlantUmlActivityParser.Parse("""
            @startuml
            :task "PI must review" claim="review:ae" resolvedBy="authorityDecision";
            :After;
            @enduml
            """);
        var ran = new List<string>();
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>> { ["After"] = _ => ran.Add("after") },
            new Dictionary<string, Func<JsonObject, bool>>());
        // Presence of the default correlatedBy field ("targetEventId") is
        // exactly what a real resolver event's own payload contributes to
        // the merged snapshot once it arrives -- see FlowInterpreter's own
        // IsResolved comment.
        var mergedState = new JsonObject { ["targetEventId"] = "some-event-id" };

        var outcome = interpreter.Evaluate(ast, mergedState);

        Assert.IsInstanceOfType<FlowCompleted>(outcome);
        CollectionAssert.AreEqual(new[] { "after" }, ran);
    }

    [TestMethod]
    public void APauseInsideANestedIfBranchPropagatesAllTheWayUp()
    {
        var ast = PlantUmlActivityParser.Parse("""
            @startuml
            if (SeriousAdverseEvent?) then (yes)
              :task "PI must review" claim="review:ae" resolvedBy="authorityDecision";
              :Fold now;
            else (no)
              :Fold immediately;
            endif
            :Never reached;
            @enduml
            """);
        var ran = new List<string>();
        var interpreter = new FlowInterpreter(
            new Dictionary<string, Action<JsonObject>>
            {
                ["Fold now"] = _ => ran.Add("fold-now"),
                ["Fold immediately"] = _ => ran.Add("fold-immediate"),
                ["Never reached"] = _ => ran.Add("never-reached"),
            },
            new Dictionary<string, Func<JsonObject, bool>>());

        var outcome = interpreter.Evaluate(ast, new JsonObject { ["SeriousAdverseEvent"] = true });

        Assert.IsInstanceOfType<FlowPausedAtTask>(outcome);
        Assert.AreEqual(0, ran.Count); // neither "fold now" nor "never reached" ran
    }

    private static FlowDefinition BuildAdverseEventReviewFlow() => FlowDefinition.Parse(
        name: "vitals-workflow-b-adverse-event-review",
        raiserEventType: "AdverseEventReported",
        appId: "trial1",
        entityIdField: "$.AeId",
        pumlSource: """
            @startuml
            :Coordinator publishes AdverseEventReported;
            if (SeriousAdverseEvent?) then (yes)
              :task "PI must review the adverse event" claim="review:ae" resolvedBy="authorityDecision";
              :Fold now (catch-up);
            else (no)
              :Fold immediately (Full);
            endif
            @enduml
            """,
        actions: new Dictionary<string, Action<JsonObject>>
        {
            ["Coordinator publishes AdverseEventReported"] = _ => { },
            ["Fold now (catch-up)"] = _ => { },
            ["Fold immediately (Full)"] = _ => { },
        });

    [TestMethod]
    public void FlowProjectionKeysARaiserEventByItsOwnEventId()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());
        var eventId = Guid.NewGuid();

        var key = projection.GetKey("AdverseEventReported", eventId, new JsonObject());

        Assert.AreEqual(eventId.ToString(), key);
    }

    [TestMethod]
    public void FlowProjectionKeysAResolverEventByItsCorrelatedByField()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());
        var payload = new JsonObject { ["targetEventId"] = "raiser-key-123" };

        var key = projection.GetKey("authorityDecision", Guid.NewGuid(), payload);

        Assert.AreEqual("raiser-key-123", key);
    }

    [TestMethod]
    public void FlowProjectionForcesPartialForResolverTypesWithoutTouchingTheRaiserType()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());

        Assert.IsNull(projection.OverrideChangeKind("AdverseEventReported"));
        Assert.AreEqual(EventStore.Projections.Abstractions.ChangeKind.Partial, projection.OverrideChangeKind("authorityDecision"));
    }

    [TestMethod]
    public void FlowProjectionProjectsAPendingTaskForASeriousUnresolvedAdverseEvent()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());
        var mergedState = new JsonObject { ["AeId"] = "ae-1", ["SeriousAdverseEvent"] = true };

        var task = projection.Project("ae-1", mergedState);

        Assert.IsNotNull(task);
        Assert.AreEqual("PI must review the adverse event", task!.Description);
        Assert.AreEqual("review:ae", task.RequiredClaim);
        Assert.AreEqual("trial1", task.AppId);
        Assert.AreEqual("ae-1", task.EntityId);
    }

    [TestMethod]
    public void FlowProjectionReturnsNullOnceTheTaskIsResolved()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());
        var mergedState = new JsonObject { ["AeId"] = "ae-1", ["SeriousAdverseEvent"] = true, ["targetEventId"] = "ae-1" };

        Assert.IsNull(projection.Project("ae-1", mergedState));
    }

    [TestMethod]
    public void FlowProjectionReturnsNullForANonSeriousEventThatNeverReachesTheTask()
    {
        var projection = new FlowProjection(BuildAdverseEventReviewFlow());
        var mergedState = new JsonObject { ["AeId"] = "ae-2", ["SeriousAdverseEvent"] = false };

        Assert.IsNull(projection.Project("ae-2", mergedState));
    }
}
