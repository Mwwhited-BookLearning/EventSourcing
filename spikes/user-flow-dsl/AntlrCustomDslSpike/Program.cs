using Antlr4.Runtime;
using AntlrCustomDslSpike;

// Loads the REAL, committed .flow file -- a real standalone DSL source file,
// never an inline C# string literal, per direct request. Copied next to the
// built binary as a Content item (see AntlrCustomDslSpike.csproj).
var flowPath = Path.Combine(AppContext.BaseDirectory, "Flows", "AdverseEventReview.flow");
var source = File.ReadAllText(flowPath);

var lexer = new UserFlowDslLexer(new AntlrInputStream(source));
var parser = new UserFlowDslParser(new CommonTokenStream(lexer));
var flowContext = parser.flow();

var builder = new FlowAstBuilderVisitor();
var ast = flowContext.step().Select(builder.VisitStep).ToList();

Console.WriteLine($"Parsed {CountNodes(ast)} node(s) from: {flowPath}");
Console.WriteLine();

Console.WriteLine("=== Scenario 1: SeriousAdverseEvent = true, PI decision = accepted ===");
Run(ast, seriousAdverseEvent: true, accepted: true);

Console.WriteLine();
Console.WriteLine("=== Scenario 2: SeriousAdverseEvent = true, PI decision = rejected ===");
Run(ast, seriousAdverseEvent: true, accepted: false);

Console.WriteLine();
Console.WriteLine("=== Scenario 3: SeriousAdverseEvent = false (ordinary publish) ===");
Run(ast, seriousAdverseEvent: false, accepted: false);

static void Run(IReadOnlyList<FlowNode> ast, bool seriousAdverseEvent, bool accepted)
{
    // Stand-ins for this project's own real primitives (ADR-035/042/043/066)
    // -- a real integration would call PublishService/RoleService/etc.
    // directly; this spike proves the PARSER+INTERPRETER shape, not a full
    // end-to-end wire-up against the running EventStore stack.
    var actions = new Dictionary<string, Action>
    {
        ["Coordinator publishes AdverseEventReported"] = () => Console.WriteLine("  -> POST /publish/AdverseEventReported"),
        ["AuthorityStatus = pending_review"] = () => Console.WriteLine("  -> AuthorityStatus set to pending_review (ADR-035/042)"),
        ["PI delegates scoped secondary opinion access (ADR-043)"] = () => Console.WriteLine("  -> UCAN delegation issued, scoped to this entity (ADR-043)"),
        ["Colleague reviews via delegated read"] = () => Console.WriteLine("  -> Colleague reads the pending finding via the delegated grant"),
        ["PI publishes authorityDecision, step-up required (ADR-066)"] = () => Console.WriteLine("  -> POST /publish/authorityDecision (RFC 9470 step-up enforced, ADR-066)"),
        ["Fold now (catch-up)"] = () => Console.WriteLine("  -> Entity Store folds the finding now (accepted)"),
        ["Entity Store left untouched"] = () => Console.WriteLine("  -> Entity Store left untouched (rejected)"),
        ["Fold immediately (Full)"] = () => Console.WriteLine("  -> Entity Store folds immediately, Full (ordinary, non-serious publish)"),
    };
    var conditions = new Dictionary<string, Func<bool>>
    {
        ["SeriousAdverseEvent"] = () => seriousAdverseEvent,
        ["accepted"] = () => accepted,
    };

    new FlowInterpreter(actions, conditions).Run(ast);
}

static int CountNodes(IReadOnlyList<FlowNode> nodes) => nodes.Sum(n => n switch
{
    IfNode i => 1 + CountNodes(i.ThenSteps) + CountNodes(i.ElseSteps),
    _ => 1,
});
