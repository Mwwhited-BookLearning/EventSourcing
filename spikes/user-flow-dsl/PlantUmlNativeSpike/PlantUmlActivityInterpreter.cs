namespace PlantUmlNativeSpike;

// The "explicit registration, no reflection scanning" discipline
// docs/patterns/composition-root-and-pure-di.md already names -- each
// action label / condition string in the diagram resolves against a
// small, explicit dictionary the caller builds, never a convention-based
// lookup.
public sealed class PlantUmlActivityInterpreter(
    IReadOnlyDictionary<string, Action> actions,
    IReadOnlyDictionary<string, Func<bool>> conditions)
{
    public void Run(IReadOnlyList<ActivityNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ActionNode action:
                    if (!actions.TryGetValue(action.Label, out var handler))
                        throw new InvalidOperationException($"No registered handler for action label: \"{action.Label}\"");
                    handler();
                    break;

                case IfNode ifNode:
                    if (!conditions.TryGetValue(ifNode.Condition, out var predicate))
                        throw new InvalidOperationException($"No registered predicate for condition: \"{ifNode.Condition}\"");
                    Run(predicate() ? ifNode.ThenBranch : ifNode.ElseBranch);
                    break;

                case StopNode:
                    return;
            }
        }
    }
}
