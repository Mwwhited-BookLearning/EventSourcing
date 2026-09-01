using net.adamec.lib.common.dmn.engine.engine.execution.context;
using net.adamec.lib.common.dmn.engine.parser;
using net.adamec.lib.common.dmn.engine.parser.dto;

namespace NRulesDmnSpike;

// The one genuine multi-factor decision in this scenario -- severity score
// AND event type both bearing on the routing outcome -- delegated to a real
// DMN 1.3 decision table (Dmn/AdverseEventClassification.dmn) instead of an
// inline C# condition, docs/comparisons/user-flow-dsl.md Option E.
public sealed class DmnAdverseEventClassifier(string dmnFilePath) : IAdverseEventClassifier
{
    private readonly DmnModel _model = DmnParser.Parse13(dmnFilePath);

    public string Classify(int severityScore, string eventType)
    {
        var ctx = DmnExecutionContextFactory.CreateExecutionContext(_model);
        ctx.WithInputParameter("SeverityScore", severityScore);
        ctx.WithInputParameter("EventType", eventType);
        var result = ctx.ExecuteDecision("Classify Adverse Event");
        return (string)result.First["ReviewPath"].Value;
    }
}
