using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EventStore.Flows;

public abstract record FlowWalkOutcome;

public sealed record FlowCompleted : FlowWalkOutcome;

public sealed record FlowPausedAtTask(TaskDeclaration Task) : FlowWalkOutcome;

// Generalizes spikes/user-flow-dsl/PlantUmlNativeSpike/
// PlantUmlActivityInterpreter.cs (ADR-101): the spike's toy Func<bool>/Action
// delegates had no payload access; a real flow evaluates against an event's
// actual merged JSON. Explicit registration, no reflection scanning
// (docs/patterns/composition-root-and-pure-di.md), exactly like the spike.
//
// Critical framing (ADR-101's own load-bearing design call): this does NOT
// step through the diagram once and remember where it stopped. It walks the
// WHOLE diagram, statelessly, from the top, every time it's called --
// against whatever the entity's current merged snapshot is. Reaching an
// unresolved task node is simply whatever the current state implies right
// now; there is no separate "flow instance position" stored anywhere.
public sealed class FlowInterpreter(
    IReadOnlyDictionary<string, Action<JsonObject>> actions,
    IReadOnlyDictionary<string, Func<JsonObject, bool>> conditions)
{
    private static readonly Regex FieldConditionPattern = new(@"^[A-Za-z][A-Za-z0-9]*\?$", RegexOptions.Compiled);

    public FlowWalkOutcome Evaluate(IReadOnlyList<ActivityNode> nodes, JsonObject mergedState)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ActionNode action when TaskDeclaration.TryParse(action.Label, out var task):
                    if (!IsResolved(task!, mergedState))
                        return new FlowPausedAtTask(task!);
                    break; // already resolved -- narrate nothing, continue past it

                case ActionNode action:
                    if (!actions.TryGetValue(action.Label, out var handler))
                        throw new InvalidOperationException($"No registered handler for action label: \"{action.Label}\"");
                    handler(mergedState);
                    break;

                case IfNode ifNode:
                    var branchOutcome = Evaluate(EvaluateCondition(ifNode.Condition, mergedState) ? ifNode.ThenBranch : ifNode.ElseBranch, mergedState);
                    if (branchOutcome is FlowPausedAtTask)
                        return branchOutcome;
                    break;

                case StopNode:
                    return new FlowCompleted();
            }
        }

        return new FlowCompleted();
    }

    private bool EvaluateCondition(string condition, JsonObject mergedState)
    {
        if (conditions.TryGetValue(condition, out var predicate))
            return predicate(mergedState);

        if (!FieldConditionPattern.IsMatch(condition))
            throw new InvalidOperationException($"No registered predicate for condition: \"{condition}\"");

        // The generic field-truthy rule (ADR-101): covers both a boolean
        // gate field (e.g. SeriousAdverseEvent) and a decision-outcome
        // field (e.g. an authorityDecision's own "accepted"/"rejected"
        // string) -- the only two real shapes the converted flows need.
        // Anything else must be registered explicitly in `conditions`.
        var fieldName = condition[..^1];
        return mergedState[fieldName] switch
        {
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            JsonValue v when v.TryGetValue<string>(out var s) => s == "accepted",
            _ => false,
        };
    }

    // A resolver event's own payload necessarily carries its correlation
    // field (that's literally what routed it to this key in the first
    // place, see FlowProjection.GetKey) -- a raiser event's payload never
    // will. So the mere presence of `task.CorrelatedBy` in the merged state
    // is itself the fully generic "has any resolvedBy-type event arrived
    // for this key yet" signal, with no per-domain payload-shape knowledge
    // needed.
    private static bool IsResolved(TaskDeclaration task, JsonObject mergedState) =>
        mergedState[task.CorrelatedBy] is not null;
}
