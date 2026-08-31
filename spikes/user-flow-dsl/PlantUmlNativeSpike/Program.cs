using PlantUmlNativeSpike;

// Loads the REAL, already-committed .puml file -- the exact same one
// docs/comparisons/user-flow-dsl.md's Option F/G1 sections show -- no
// separate copy, proving "the same file is the diagram, the
// documentation, and the execution" literally, not just as a claim.
var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var pumlPath = Path.Combine(repoRoot, "docs", "diagrams", "comparisons", "user-flow-dsl",
    "01-option-f-hand-authored-plantuml-activity-diagrams-.puml");
var source = File.ReadAllText(pumlPath);
var ast = PlantUmlActivityParser.Parse(source);

Console.WriteLine($"Parsed {CountNodes(ast)} node(s) from: {pumlPath}");
Console.WriteLine();

// Scenario 1: a serious adverse event, PI accepts the finding.
Console.WriteLine("=== Scenario 1: SeriousAdverseEvent = true, PI decision = accepted ===");
Run(ast, seriousAdverseEvent: true, accepted: true);

Console.WriteLine();
Console.WriteLine("=== Scenario 2: SeriousAdverseEvent = true, PI decision = rejected ===");
Run(ast, seriousAdverseEvent: true, accepted: false);

Console.WriteLine();
Console.WriteLine("=== Scenario 3: SeriousAdverseEvent = false (ordinary publish) ===");
Run(ast, seriousAdverseEvent: false, accepted: false);

static void Run(IReadOnlyList<ActivityNode> ast, bool seriousAdverseEvent, bool accepted)
{
    // Stand-ins for this project's own real primitives (ADR-035/042/043/066)
    // -- a real integration would call PublishService/RoleService/etc.
    // directly; this spike proves the INTERPRETER shape, not a full
    // end-to-end wire-up against the running EventStore stack.
    //
    // Two action labels below use a doubled backslash ("\\n"), not a
    // single "\n" -- found only by actually running this: a C# "\n"
    // string literal IS a real newline character, which does not match
    // the .puml file's own literal two-character escape ("\" then "n",
    // PlantUML's own line-break syntax). The first version of this file
    // used a single "\n" here and failed at runtime with "no registered
    // handler," the exact class of bug this whole exercise exists to
    // surface -- fixed here, not hidden.
    var actions = new Dictionary<string, Action>
    {
        ["Coordinator publishes AdverseEventReported"] = () => Console.WriteLine("  -> POST /publish/AdverseEventReported"),
        ["AuthorityStatus = pending_review"] = () => Console.WriteLine("  -> AuthorityStatus set to pending_review (ADR-035/042)"),
        ["PI delegates scoped\\n\"secondary opinion\" access (ADR-043)"] = () => Console.WriteLine("  -> UCAN delegation issued, scoped to this entity (ADR-043)"),
        ["Colleague reviews via delegated read"] = () => Console.WriteLine("  -> Colleague reads the pending finding via the delegated grant"),
        ["PI publishes authorityDecision\\n(step-up required, ADR-066)"] = () => Console.WriteLine("  -> POST /publish/authorityDecision (RFC 9470 step-up enforced, ADR-066)"),
        ["Fold now (catch-up)"] = () => Console.WriteLine("  -> Entity Store folds the finding now (accepted)"),
        ["Entity Store left untouched"] = () => Console.WriteLine("  -> Entity Store left untouched (rejected)"),
        ["Fold immediately (Full)"] = () => Console.WriteLine("  -> Entity Store folds immediately, Full (ordinary, non-serious publish)"),
    };
    var conditions = new Dictionary<string, Func<bool>>
    {
        ["SeriousAdverseEvent?"] = () => seriousAdverseEvent,
        ["accepted?"] = () => accepted,
    };

    new PlantUmlActivityInterpreter(actions, conditions).Run(ast);
}

static int CountNodes(IReadOnlyList<ActivityNode> nodes) => nodes.Sum(n => n switch
{
    IfNode i => 1 + CountNodes(i.ThenBranch) + CountNodes(i.ElseBranch),
    _ => 1,
});

static string FindRepoRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null && !File.Exists(Path.Combine(dir, "EventStore.slnx")))
        dir = Directory.GetParent(dir)?.FullName;
    return dir ?? throw new InvalidOperationException("Could not find repo root (EventStore.slnx not found in any parent directory of " + startDir + ")");
}
