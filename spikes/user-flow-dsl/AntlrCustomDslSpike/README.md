# ANTLR custom DSL spike — Option G2

Proves the "wholly custom textual DSL" option from
[`docs/comparisons/user-flow-dsl.md`](../../../docs/comparisons/user-flow-dsl.md):
a real ANTLR4 grammar, compiled by the real ANTLR4 Java tool via the
`Antlr4BuildTasks` NuGet package (no manual Java/Docker setup), generating
a Visitor-pattern parser (not Listener, per direct request) that walks a
real `.flow` source file into the same small AST/interpreter shape the
other spikes in this folder use.

## How the two file kinds relate

This spike has two distinct kinds of file, the same relationship a JSON
Schema has to a JSON document, or an XSD has to an XML file — one schema,
many conforming instances:

| File | Role | Named for |
|---|---|---|
| `Grammar/UserFlowDsl.g4` | **The schema.** Defines the DSL's own grammar — what a `flow` is made of (`step+`), what a `step` can be (`action` or `ifStep`), and their exact token syntax. One file, shared by every flow written in this language. | The DSL itself |
| `Flows/*.flow` | **An instance.** Real source text written *in* that grammar — a specific flow. Nothing about the grammar changes based on which instance is loaded. | The specific scenario it encodes |

Concretely: `Grammar/UserFlowDsl.g4` never mentions "adverse event" or
"authority decision" anywhere — it only defines `flow`/`step`/`action`/
`ifStep`/`STRING`/`WS`, the general shape any flow in this DSL must take.
`Flows/AdverseEventReview.flow` is the one instance currently in this
repo, mirroring the same Vitals adverse-event-review scenario the other
spikes in this folder execute, so results are comparable across options.

Adding a second scenario means adding a second `.flow` file under
`Flows/` (or another folder) — the grammar itself does not change, the
same way adding a new JSON document doesn't change its JSON Schema.

## Grammar shape

```antlr
flow: step+ EOF;

step
    : action
    | ifStep
    ;

action: 'do' STRING ';';

ifStep: 'if' STRING 'then' '{' thenSteps+=step+ '}' ('else' '{' elseSteps+=step+ '}')?;

STRING: '"' ~["\r\n]* '"';
WS: [ \t\r\n]+ -> skip;
```

`action`'s `STRING` is an opaque label — resolved against an explicit
`IReadOnlyDictionary<string, Action>` at interpretation time, the same
"no reflection scanning, explicit registration" discipline
[`docs/patterns/composition-root-and-pure-di.md`](../../../docs/patterns/composition-root-and-pure-di.md)
already names, and the same shape `PlantUmlNativeSpike`'s interpreter
uses for its own action/condition registry. `ifStep`'s condition
`STRING` resolves the same way against an `IReadOnlyDictionary<string,
Func<bool>>`.

The `thenSteps+=` / `elseSteps+=` labels are what make the optional
`else` branch distinguishable in the generated parse tree — without
them, ANTLR merges every `step` under an `ifStep` (both branches) into
one flat array with no marker for which side of `else` each belongs to.
Labeling the two repetitions separately makes ANTLR generate
`IfStepContext._thenSteps`/`._elseSteps` as two separate lists instead,
which `FlowAstBuilderVisitor.VisitIfStep` reads directly — no manual
child-splitting needed.

## Pipeline

1. `Antlr4BuildTasks` compiles `Grammar/UserFlowDsl.g4` at build time into
   `UserFlowDslLexer`/`UserFlowDslParser`/`UserFlowDslVisitor`/
   `UserFlowDslBaseVisitor` (generated into `obj/`, not committed —
   covered by the repo's root `.gitignore`).
2. `Program.cs` reads `Flows/AdverseEventReview.flow` as a real file
   (copied next to the built binary as a `Content` item — never an
   inline C# string literal, per direct request), lexes/parses it, then
   walks the parse tree with `FlowAstBuilderVisitor` (`UserFlowDslBaseVisitor<FlowNode>`)
   into the `FlowNode`/`ActionNode`/`IfNode` records in `FlowAst.cs`.
3. `FlowInterpreter` runs that AST against the same three scenarios
   (accepted / rejected / non-serious) the other spikes in this folder
   use, resolving each action/condition string against an explicit
   dictionary built in `Program.cs`.

Run with `dotnet run` from this directory.

## Findings

Worked on the first real run once the grammar itself was correct — no
runtime API-mismatch surprises the way the Elsa spike had (see the
parent [`README.md`](../README.md)), because a hand-written grammar has
no upstream library whose real API can drift from its docs. The real,
worth-naming cost is upfront instead: every construct (`if`/`then`/
`else`, string-quoted labels, the exact token set) had to be designed
and implemented by hand — nothing pre-built to lean on, unlike Options B
(Elsa) and G1 (PlantUML) which parse an existing, already-designed
notation.

Two grammar-design decisions worth calling out for anyone extending this
spike:
- The `thenSteps+=`/`elseSteps+=` labeling above, without which the
  Visitor would need to reconstruct the then/else split itself from the
  token stream.
- The grammar is named for the DSL (`UserFlowDsl`), not for the one
  scenario it happens to be demonstrated with — `AdverseEventReview.flow`
  is just one instance written against it, not the grammar's namesake.
  A second scenario would live as a second `.flow` file, no grammar
  change required.
