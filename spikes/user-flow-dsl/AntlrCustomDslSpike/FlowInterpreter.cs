namespace AntlrCustomDslSpike;

// The same "explicit registration, no reflection scanning" discipline
// docs/patterns/composition-root-and-pure-di.md names, and the same shape
// PlantUmlNativeSpike/PlantUmlActivityInterpreter.cs uses for its own
// action/condition registry, applied here to the ANTLR-built AST instead.
public sealed class FlowInterpreter(
    IReadOnlyDictionary<string, Action> actions,
    IReadOnlyDictionary<string, Func<bool>> conditions)
{
    public void Run(IReadOnlyList<FlowNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ActionNode action:
                    if (!actions.TryGetValue(action.Text, out var handler))
                        throw new InvalidOperationException($"No registered handler for action: \"{action.Text}\"");
                    handler();
                    break;

                case IfNode ifNode:
                    if (!conditions.TryGetValue(ifNode.Condition, out var predicate))
                        throw new InvalidOperationException($"No registered predicate for condition: \"{ifNode.Condition}\"");
                    Run(predicate() ? ifNode.ThenSteps : ifNode.ElseSteps);
                    break;
            }
        }
    }
}
