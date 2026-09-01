namespace EventStore.Flows;

// ADR-101: builds the small ActivityAst (ActionNode/IfNode/StopNode)
// bottom-up as ParseTreeWalker walks the real ANTLR-generated tree --
// the standard idiomatic technique for a Listener (not Visitor, per direct
// request): since Enter/Exit callbacks return nothing, each rule's own
// finished ActivityNode is stashed in a dictionary keyed by its parse-tree
// context the moment that rule exits, and a parent rule's own Exit method
// reads its already-built children back out of that same dictionary --
// always available by the time the parent needs them, since ANTLR always
// finishes a child before its parent.
public sealed class ActivityAstBuilderListener : PlantUmlActivityDiagramBaseListener
{
    private readonly Dictionary<Antlr4.Runtime.ParserRuleContext, ActivityNode> _nodes = new();
    private IReadOnlyList<ActivityNode>? _result;

    public IReadOnlyList<ActivityNode> Result =>
        _result ?? throw new InvalidOperationException($"{nameof(ActivityAstBuilderListener)} has not walked a parse tree yet.");

    public override void ExitActionStep(PlantUmlActivityDiagramParser.ActionStepContext context)
    {
        var text = context.action().ACTION_TEXT().GetText();
        _nodes[context] = new ActionNode(text[1..^1]); // strip the leading ':' and trailing ';'
    }

    public override void ExitStopStep(PlantUmlActivityDiagramParser.StopStepContext context)
    {
        _nodes[context] = new StopNode();
    }

    public override void ExitIfStepAlt(PlantUmlActivityDiagramParser.IfStepAltContext context)
    {
        var condition = StripParens(context.condition.Text);
        var thenBranch = context._thenSteps.Select(step => _nodes[step]).ToList();
        var elseBranch = context._elseSteps.Select(step => _nodes[step]).ToList();
        _nodes[context] = new IfNode(condition, thenBranch, elseBranch);
    }

    public override void ExitDiagram(PlantUmlActivityDiagramParser.DiagramContext context)
    {
        _result = context.step().Select(step => _nodes[step]).ToList();
    }

    private static string StripParens(string text) => text[1..^1];
}
