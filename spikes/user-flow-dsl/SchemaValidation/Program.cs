using System.Xml;
using System.Xml.Schema;
using System.Xml.Xsl;

var baseDir = AppContext.BaseDirectory;
var repoRoot = FindRepoRoot(baseDir);
var spikesDir = Path.Combine(repoRoot, "spikes", "user-flow-dsl");

// Real, official OMG schemas (see Schemas/README.md for exact source URLs),
// checked in as real files under Schemas/, never re-typed by hand.
var dmnSchemaSet = LoadSchemaSet(Path.Combine(baseDir, "Schemas", "dmn", "DMN13.xsd"));
var bpmnSchemaSet = LoadSchemaSet(Path.Combine(baseDir, "Schemas", "bpmn", "BPMN20.xsd"));

var failures = 0;

failures += ValidateAgainstSchema(
    Path.Combine(spikesDir, "NRulesDmnSpike", "Dmn", "AdverseEventClassification.dmn"),
    dmnSchemaSet);

failures += ValidateAgainstSchema(
    Path.Combine(spikesDir, "ZeebeSpike", "Bpmn", "AdverseEventReview.bpmn"),
    bpmnSchemaSet);

failures += ValidateAgainstSchema(
    Path.Combine(spikesDir, "PlantBpmnSpike", "Generated", "AdverseEventReviewFlat.bpmn"),
    bpmnSchemaSet);

failures += ValidateAgainstSchema(
    Path.Combine(spikesDir, "PlantBpmnSpike", "Generated", "AdverseEventReview.bpmn"),
    bpmnSchemaSet);

// No canonical XSD/RelaxNG exists for an XSLT stylesheet itself (unlike
// DMN/BPMN, XSLT's own W3C recommendation isn't distributed as a single
// normative schema for this purpose) -- the practical equivalent of
// "is this file valid" is "does a real XSLT processor accept it,"
// exactly what PlantBpmnSpike/Program.cs already does at runtime.
failures += ValidateXslt(Path.Combine(spikesDir, "PlantBpmnSpike", "Xslt", "BpmnToPlantUml.xslt"));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

static XmlSchemaSet LoadSchemaSet(string rootXsdPath)
{
    // .NET's XmlSchemaSet does not resolve xsd:import/xsd:include by
    // default (XmlResolver defaults to null for modern .NET's usual
    // "don't fetch arbitrary URIs" posture) -- without this, the real
    // OMG schemas' own cross-file imports (DMN13.xsd -> DMNDI13.xsd ->
    // DC.xsd/DI.xsd, BPMN20.xsd -> BPMNDI.xsd/Semantic.xsd -> DC.xsd/DI.xsd)
    // silently fail to resolve, surfacing as a confusing "element is not
    // declared" compile error instead of a file-not-found. All the schema
    // files these downloads need are real, checked-in, local siblings, so
    // this is exactly the safe, intended use of XmlUrlResolver here.
    var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
    set.Add(null, rootXsdPath);
    set.Compile();
    return set;
}

static int ValidateAgainstSchema(string xmlPath, XmlSchemaSet schemaSet)
{
    Console.WriteLine($"Validating {Path.GetRelativePath(AppContext.BaseDirectory, xmlPath)} ...");
    var errors = new List<string>();
    var settings = new XmlReaderSettings
    {
        ValidationType = ValidationType.Schema,
        Schemas = schemaSet,
    };
    settings.ValidationEventHandler += (_, e) => errors.Add($"  [{e.Severity}] {e.Message}");

    using var reader = XmlReader.Create(xmlPath, settings);
    while (reader.Read())
    {
    }

    if (errors.Count == 0)
    {
        Console.WriteLine("  OK");
        return 0;
    }

    foreach (var error in errors)
        Console.WriteLine(error);
    return 1;
}

static int ValidateXslt(string xsltPath)
{
    Console.WriteLine($"Validating {Path.GetRelativePath(AppContext.BaseDirectory, xsltPath)} (compiles as a real XSLT 1.0 stylesheet) ...");
    try
    {
        new XslCompiledTransform().Load(xsltPath);
        Console.WriteLine("  OK");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [Error] {ex.Message}");
        return 1;
    }
}

static string FindRepoRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null && !File.Exists(Path.Combine(dir, "EventStore.slnx")))
        dir = Directory.GetParent(dir)?.FullName;
    return dir ?? throw new InvalidOperationException("Could not find repo root (EventStore.slnx not found in any parent directory of " + startDir + ")");
}
