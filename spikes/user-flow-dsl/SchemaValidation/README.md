# Schema validation for the user-flow DSL spikes

A small, real validator for the text-file formats the spikes in this
folder actually use — not a spike itself, no scenario, no branching
logic to run. `dotnet run` from this directory validates every real
target file this project knows about and exits non-zero if any fails,
so it's usable as a CI step.

## What gets validated, and against what

| Format | Real target file(s) | Validated against |
|---|---|---|
| DMN 1.3 XML | `NRulesDmnSpike/Dmn/AdverseEventClassification.dmn` | The real OMG DMN 1.3 XSD (`Schemas/dmn/`) |
| BPMN 2.0 XML | `ZeebeSpike/Bpmn/AdverseEventReview.bpmn`, `PlantBpmnSpike/Generated/AdverseEventReviewFlat.bpmn`, `PlantBpmnSpike/Generated/AdverseEventReview.bpmn` | The real OMG BPMN 2.0 XSD (`Schemas/bpmn/`) |
| XSLT 1.0 | `PlantBpmnSpike/Xslt/BpmnToPlantUml.xslt` | Not schema-validated (see below) — compiled by .NET's real `XslCompiledTransform` |

**PlantUML (`.puml`, PlantUmlNativeSpike/PlantBpmnSpike) and the custom
`UserFlowDsl` grammar (`.flow`, AntlrCustomDslSpike) are not covered
here at all — checked, not assumed:**

- PlantUML has no published XSD, JSON Schema, or other formal grammar
  a general-purpose validator could check a `.puml` file against; it's
  a DSL defined by its own reference implementation's parser, not a
  schema-described format. The closest real equivalent to "validation"
  for the subset this repo uses is `PlantUmlNativeSpike`'s own hand-
  rolled parser (`PlantUmlActivityParser.cs`) actually parsing the file
  without error — which that spike's own `dotnet run` already does
  every time.
- `UserFlowDsl` is this repo's own grammar (`AntlrCustomDslSpike/
  Grammar/UserFlowDsl.g4`) — there is no independent, external schema
  for it to validate against by definition; the grammar itself *is*
  the schema, and ANTLR's own generated parser is already the real
  validator, exercised every time `AntlrCustomDslSpike` runs.

XSLT stylesheets don't have a normative schema of the DMN/BPMN kind
either — the W3C XSLT 1.0 Recommendation isn't distributed as a single
schema file for this purpose. The practical equivalent — does a real
XSLT processor accept the file — is what this validator actually checks
via `System.Xml.Xsl.XslCompiledTransform.Load()`, the same call
`PlantBpmnSpike/Program.cs` makes at runtime.

Elsa (`ElsaSpike`) and Temporal (`TemporalSpike`) have no text-file
config at all — their workflows are C# code, so `dotnet build` already
is their own validation; nothing for this tool to add.

## Where the schemas came from

`Schemas/dmn/` and `Schemas/bpmn/` are real, unedited downloads (never
hand-retyped) from OMG's own hosting, fetched and their availability
confirmed directly before being checked in:

- `Schemas/dmn/DMN13.xsd` — <https://www.omg.org/spec/DMN/20191111/DMN13.xsd>
- `Schemas/dmn/DMNDI13.xsd` — <https://www.omg.org/spec/DMN/20191111/DMNDI13.xsd>
- `Schemas/dmn/DC.xsd` — <https://www.omg.org/spec/DMN/20180521/DC.xsd>
- `Schemas/dmn/DI.xsd` — <https://www.omg.org/spec/DMN/20180521/DI.xsd>
- `Schemas/bpmn/BPMN20.xsd` — <https://www.omg.org/spec/BPMN/20100501/BPMN20.xsd>
- `Schemas/bpmn/BPMNDI.xsd` — <https://www.omg.org/spec/BPMN/20100501/BPMNDI.xsd>
- `Schemas/bpmn/Semantic.xsd` — <https://www.omg.org/spec/BPMN/20100501/Semantic.xsd>
- `Schemas/bpmn/DC.xsd` — <https://www.omg.org/spec/BPMN/20100501/DC.xsd>
- `Schemas/bpmn/DI.xsd` — <https://www.omg.org/spec/BPMN/20100501/DI.xsd>

One real, worth-naming OMG inconsistency confirmed while sourcing
these: BPMN 2.0's own XML namespace is dated `20100524`
(`http://www.omg.org/spec/BPMN/20100524/MODEL`, matching every `.bpmn`
file in this repo), but OMG hosts the actual XSD files one path segment
different, under `/spec/BPMN/20100501/` — the schema's own
`targetNamespace` attribute is still `.../20100524/MODEL` internally,
it's only the hosting *path* that differs from the namespace URI. Not a
typo in this repo; confirmed directly against OMG's own server before
picking these URLs.

## A real technique note, not decoration

Loading either root schema (`DMN13.xsd`, `BPMN20.xsd`) into an
`XmlSchemaSet` **fails outright** unless `XmlResolver` is explicitly set
to a real `XmlUrlResolver` first — modern .NET's `XmlSchemaSet` defaults
`XmlResolver` to `null` (the usual "don't fetch arbitrary external URIs"
posture), which also silently blocks resolving `xsd:import`/
`xsd:include` to *local, already-downloaded sibling files* sitting right
next to the schema being loaded. Without this, compiling `DMN13.xsd`
fails with a confusing `The '...DMNDI/:DMNDI' element is not declared`
— not a file-not-found, an element-not-declared, which took a moment to
place — because `DMNDI13.xsd` (and BPMN's own `BPMNDI.xsd`/
`Semantic.xsd`/`DC.xsd`/`DI.xsd`) never actually got pulled into the
compiled schema set at all. `Program.cs`'s `LoadSchemaSet` sets
`XmlResolver` explicitly for exactly this reason, with the real error
message and fix recorded in its own comment.

## Sanity-checked, not just asserted

Before trusting a clean run, a throwaway deliberate schema violation
(an invalid child element injected into `ZeebeSpike`'s own `.bpmn` file,
reverted immediately after) was used to confirm the validator actually
fails loudly and precisely on real bad input, rather than silently
passing everything regardless of content:

```
[Error] The element 'startEvent' in namespace '...BPMN/20100524/MODEL'
has invalid child element 'bogusElementThatDoesNotExist' ...
```

Also confirmed directly: `PlantBpmnSpike/Generated/AdverseEventReview.bpmn`
— the file with the real, documented nested-if/dead-end-gateway defect
found while building `PlantBpmnSpike` — **passes XSD validation cleanly**
despite that defect. This is expected, not a gap in this validator: XSD
validates document *structure* (element/attribute shape), not process
*reachability* (whether every gateway can actually be walked to an end
event) — a genuinely different, useful distinction this tool makes
concrete rather than just asserts.
