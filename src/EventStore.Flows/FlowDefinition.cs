using System.Text.Json.Nodes;

namespace EventStore.Flows;

// One registered instance per converted workflow (ADR-101), e.g.
// VitalsWorkflowB.Flow.cs. RaiserEventType is supplied explicitly by the
// caller -- the .puml text narrates that the raiser event happened, it
// doesn't mechanically encode which real event type triggers the flow.
// Likewise AppId (registration-level metadata, e.g. VitalsWorkflowB.AppId --
// never part of a domain event's own JSON payload) and EntityIdField (the
// raiser event's own real EntityIdField from its schema registration, e.g.
// "$.AeId" for AdverseEventReported -- confirmed by reading
// VitalsWorkflowB.cs directly; NOT a fixed "EntityId" convention) must be
// supplied by the caller rather than assumed.
public sealed record FlowDefinition(
    string Name,
    string RaiserEventType,
    string AppId,
    string EntityIdField,
    IReadOnlyList<ActivityNode> Ast,
    IReadOnlyDictionary<string, Action<JsonObject>> Actions,
    IReadOnlyDictionary<string, Func<JsonObject, bool>> Conditions)
{
    public static FlowDefinition Parse(
        string name,
        string raiserEventType,
        string appId,
        string entityIdField,
        string pumlSource,
        IReadOnlyDictionary<string, Action<JsonObject>> actions,
        IReadOnlyDictionary<string, Func<JsonObject, bool>>? conditions = null) =>
        new(name, raiserEventType, appId, entityIdField, PlantUmlActivityParser.Parse(pumlSource), actions,
            conditions ?? new Dictionary<string, Func<JsonObject, bool>>());

    // Every event type any task node in this flow's AST could be resolved
    // by -- used to build FlowProjection.EventTypes (the raiser type plus
    // all of these) so ProjectionHost tails every stream this flow needs.
    public IReadOnlyList<string> CollectResolverEventTypes()
    {
        var found = new List<string>();
        Walk(Ast);
        return found.Distinct().ToList();

        void Walk(IReadOnlyList<ActivityNode> nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case ActionNode action when TaskDeclaration.TryParse(action.Label, out var task):
                        found.AddRange(task!.ResolvedByEventTypes);
                        break;
                    case IfNode ifNode:
                        Walk(ifNode.ThenBranch);
                        Walk(ifNode.ElseBranch);
                        break;
                }
            }
        }
    }

    // The task declaration(s) whose ResolvedByEventTypes include eventType --
    // used by FlowProjection.GetKey to find which CorrelatedBy field a
    // resolver event's own key should be extracted from.
    public TaskDeclaration FindTaskDeclarationFor(string eventType)
    {
        TaskDeclaration? found = null;
        Walk(Ast);
        return found ?? throw new InvalidOperationException(
            $"No task declaration in flow \"{Name}\" names \"{eventType}\" as a resolvedBy event type.");

        void Walk(IReadOnlyList<ActivityNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (found is not null)
                    return;

                switch (node)
                {
                    case ActionNode action when TaskDeclaration.TryParse(action.Label, out var task) && task!.ResolvedByEventTypes.Contains(eventType):
                        found = task;
                        return;
                    case IfNode ifNode:
                        Walk(ifNode.ThenBranch);
                        Walk(ifNode.ElseBranch);
                        break;
                }
            }
        }
    }
}
