namespace AntlrCustomDslSpike;

public abstract record FlowNode;

public sealed record ActionNode(string Text) : FlowNode;

public sealed record IfNode(string Condition, IReadOnlyList<FlowNode> ThenSteps, IReadOnlyList<FlowNode> ElseSteps) : FlowNode;
