namespace AntlrCustomDslSpike;

/// <summary>
/// Walks a UserFlowDsl parse tree into the small <see cref="FlowNode"/> AST above.
/// ANTLR Visitor pattern, not Listener, per direct request.
/// </summary>
public sealed class FlowAstBuilderVisitor : UserFlowDslBaseVisitor<FlowNode>
{
    public override FlowNode VisitFlow(UserFlowDslParser.FlowContext context)
    {
        // The grammar's top-level `flow` rule has no single AST shape of its
        // own; callers walk `context.step()` directly (see Program.cs).
        return VisitChildren(context);
    }

    public override FlowNode VisitStep(UserFlowDslParser.StepContext context)
    {
        return context.action() is { } action
            ? VisitAction(action)
            : VisitIfStep(context.ifStep());
    }

    public override FlowNode VisitAction(UserFlowDslParser.ActionContext context)
    {
        return new ActionNode(Unquote(context.STRING().GetText()));
    }

    public override FlowNode VisitIfStep(UserFlowDslParser.IfStepContext context)
    {
        var condition = Unquote(context.STRING().GetText());
        var thenSteps = context._thenSteps.Select(VisitStep).ToList();
        var elseSteps = context._elseSteps.Select(VisitStep).ToList();
        return new IfNode(condition, thenSteps, elseSteps);
    }

    private static string Unquote(string quoted) => quoted[1..^1];
}
