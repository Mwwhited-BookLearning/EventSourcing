[← Libraries index](../README.md)

# ANTLR4 (dotnet)

**What it's for:** a real lexer/parser generator — write a grammar
(`.g4`), and it generates a Lexer, Parser, and (via the Listener or
Visitor pattern) a walkable parse-tree API in the target language. The
industry-standard answer to "I need to parse a real textual language,"
not a bespoke tokenizer/recursive-descent parser hand-written per
project.

**Why bought, not built:** `docs/comparisons/user-flow-dsl.md`'s own
Option G1 spike used a hand-rolled, line-based parser for its constrained
PlantUML Activity Diagram subset — workable, but a real, permanently-
owned piece of parsing code with its own escaping/whitespace edge cases
(the spike's own one real bug was exactly a C# vs. PlantUML escaping
mismatch). Once `ADR-101` promoted the spike into the real framework, the
explicit preference became "g4 with listener and the antlr nuget package
over a hand rolled parser" — trading a small amount of generated-code
complexity for a real, tested, widely-used grammar engine rather than a
second bespoke parser this project would have to maintain forever.

## General usage

```xml
<!-- .csproj -->
<ItemGroup>
  <PackageReference Include="Antlr4BuildTasks" Version="12.14.0" />
  <PackageReference Include="Antlr4.Runtime.Standard" Version="4.13.1" />
</ItemGroup>
<ItemGroup>
  <Antlr4 Include="Grammar\MyGrammar.g4">
    <Listener>true</Listener>
    <Visitor>false</Visitor>
  </Antlr4>
</ItemGroup>
```

```csharp
var lexer = new MyGrammarLexer(CharStreams.fromString(source));
var parser = new MyGrammarParser(new CommonTokenStream(lexer));
var tree = parser.diagram(); // the grammar's own start rule

var listener = new MyAstBuilderListener();
ParseTreeWalker.Default.Walk(listener, tree); // bottom-up AST construction
```

The idiomatic Listener pattern for building an AST: a
`Dictionary<ParserRuleContext, TNode>` keyed by parse-tree context,
populated bottom-up as each `ExitXxx` override fires (children are
already built by the time a parent rule exits), rather than a top-down
Visitor that has to explicitly recurse into each child itself.

## Where this project uses it

`ADR-101` — `EventStore.Flows`' `PlantUmlActivityDiagram.g4` grammar
parses a constrained PlantUML Activity Diagram subset
(`@startuml`/`start`/`stop`/`:action;`/`if (cond) then (yes) ... else
(no) ... endif`/`@enduml`) into `ActivityAst.cs`'s
`ActionNode`/`IfNode`/`StopNode` records, via a generated
`PlantUmlActivityDiagramBaseListener` subclass
(`ActivityAstBuilderListener.cs`). The grammar is a strict superset of
the earlier hand-rolled parser's own subset (whitespace/newlines are
skippable anywhere, not line-sensitive), so nothing that worked before
regressed.

## Links

- [antlr.org](https://www.antlr.org/)
- [github.com/antlr/antlr4](https://github.com/antlr/antlr4)
- [Antlr4BuildTasks (NuGet)](https://www.nuget.org/packages/Antlr4BuildTasks)
