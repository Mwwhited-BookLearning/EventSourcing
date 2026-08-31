namespace PlantUmlNativeSpike;

// Option G1's whole AST -- deliberately small, matching the "constrained
// subset" scope docs/comparisons/user-flow-dsl.md names: start/stop,
// :action;, if (cond?) then (yes) ... else (no) ... endif. Nothing else.
public abstract record ActivityNode;

public sealed record ActionNode(string Label) : ActivityNode;

public sealed record IfNode(string Condition, IReadOnlyList<ActivityNode> ThenBranch, IReadOnlyList<ActivityNode> ElseBranch) : ActivityNode;

public sealed record StopNode : ActivityNode;
