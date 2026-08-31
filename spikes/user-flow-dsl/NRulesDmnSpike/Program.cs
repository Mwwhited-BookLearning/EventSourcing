using NRules;
using NRules.Extensibility;
using NRules.Fluent;
using NRulesDmnSpike;

// Real DMN 1.3 file, loaded once and shared across scenarios (see
// DmnAdverseEventClassifier.cs) -- a real standalone standard XML file,
// never an inline C# string literal, per direct request.
var dmnPath = Path.Combine(AppContext.BaseDirectory, "Dmn", "AdverseEventClassification.dmn");
var classifier = new DmnAdverseEventClassifier(dmnPath);

var repository = new RuleRepository();
repository.Load(x => x.From(typeof(ClassifyEventRule).Assembly));
var factory = repository.Compile();
factory.DependencyResolver = new SingleInstanceDependencyResolver(classifier);

Console.WriteLine("=== Scenario 1: SeriousAdverseEvent = true, PI decision = accepted ===");
Run(factory, entityId: "ae-1", severityScore: 8, eventType: "Respiratory", authorityDecision: true);

Console.WriteLine();
Console.WriteLine("=== Scenario 2: SeriousAdverseEvent = true, PI decision = rejected ===");
Run(factory, entityId: "ae-2", severityScore: 8, eventType: "Respiratory", authorityDecision: false);

Console.WriteLine();
Console.WriteLine("=== Scenario 3: SeriousAdverseEvent = false (ordinary publish) ===");
Run(factory, entityId: "ae-3", severityScore: 2, eventType: "Respiratory", authorityDecision: null);

static void Run(ISessionFactory factory, string entityId, int severityScore, string eventType, bool? authorityDecision)
{
    var session = factory.CreateSession();

    Console.WriteLine("  -> POST /publish/AdverseEventReported");
    session.Insert(new AdverseEventReported(entityId, severityScore, eventType));
    session.Fire();

    // The rule chain above naturally pauses once it reaches
    // AuthorityDecisionRequested with no matching AuthorityDecisionPublished
    // fact yet -- inserting it here and firing again is the "outside input
    // arrives, resume" step, the same role Elsa's own blocking activity/
    // bookmark plays, but requiring no purpose-built blocking primitive:
    // the RETE network simply has nothing left to match until this fact exists.
    if (authorityDecision is { } accepted)
    {
        session.Insert(new AuthorityDecisionPublished(entityId, accepted));
        session.Fire();
    }
}

file sealed class SingleInstanceDependencyResolver(object instance) : IDependencyResolver
{
    public object Resolve(IResolutionContext context, Type serviceType) => instance;
}
