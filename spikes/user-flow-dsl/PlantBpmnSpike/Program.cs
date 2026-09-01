using System.Xml;
using System.Xml.Xsl;

var baseDir = AppContext.BaseDirectory;
var xsltPath = Path.Combine(baseDir, "Xslt", "BpmnToPlantUml.xslt");

// Real .NET XSLT 1.0 engine (System.Xml.Xsl.XslCompiledTransform), applied
// to real, unedited BPMN files an actual PlantBPMN run produced -- not
// hand-simulated strings. See this spike's own README.md for how each
// Generated/*.bpmn file was produced and what each run below demonstrates.
var transform = new XslCompiledTransform();
transform.Load(xsltPath);

void RunTransform(string bpmnFileName)
{
    var bpmnPath = Path.Combine(baseDir, "Generated", bpmnFileName);
    using var reader = XmlReader.Create(bpmnPath);
    using var stringWriter = new StringWriter();
    transform.Transform(reader, null, stringWriter);
    Console.WriteLine(stringWriter.ToString());
}

Console.WriteLine("=== Flat (single-level if/else) -- correct round trip ===");
Console.WriteLine("Source: Puml/AdverseEventReviewFlat.puml -> PlantBPMN -> Generated/AdverseEventReviewFlat.bpmn -> XSLT -> PlantUML");
Console.WriteLine();
RunTransform("AdverseEventReviewFlat.bpmn");

Console.WriteLine("=== Nested if/else -- PlantBPMN's own real generation defect ===");
Console.WriteLine("Source: Puml/AdverseEventReview.puml (has a nested if inside the outer 'yes' branch)");
Console.WriteLine("The inner if/else's join gateway comes out with ZERO outgoing sequenceFlows in");
Console.WriteLine("PlantBPMN's own generated BPMN -- a real dead end, not an XSLT bug. Below, the");
Console.WriteLine("outer 'yes' branch prints empty instead of the four actions + inner if/else it");
Console.WriteLine("should contain. See this spike's own README.md for the full trace.");
Console.WriteLine();
RunTransform("AdverseEventReview.bpmn");
