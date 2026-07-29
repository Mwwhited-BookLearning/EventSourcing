[← Libraries index](../README.md)

# CEL for .NET (dotnet) — candidates, not yet locked in

**What it's for:** Common Expression Language (CEL, Google) is a small,
non-Turing-complete expression language designed to evaluate untrusted
expressions quickly and safely — the declarative counterpart to
[Jint](jint.md)'s general-purpose JS sandbox, for the common upcast-
mapping case that's genuinely just field renames/simple derivations with
no real programming needed.

**Why bought, not built — with an honest caveat:** `ADR-018`/`ADR-037`
call for CEL specifically as the declarative half of upcast mapping, but
**the .NET CEL ecosystem is genuinely fragmented**, unlike every other
library in this catalog: at least four differently-maintained packages
exist (`Cel.NET`, `Cel`, `cel-net`, `cel-csharp`), each a community port
rather than one obvious, dominant implementation the way HotChocolate or
EF Core are in their categories. This is stated plainly rather than
picking one arbitrarily and implying it's a settled choice.

## General usage (illustrative — API differs across candidates)

```csharp
// Shape common across the candidates: compile once, evaluate many times
var expr = celEnvironment.Compile("event.FirstName + ' ' + event.LastName");
var result = expr.Evaluate(new { FirstName = "A.", LastName = "Smith" });
```

**See [the CEL vs. JSONata comparison](../../comparisons/upcast-transform-language.md)**
for the full head-to-head, including the honest tension it surfaces:
CEL fits this problem's shape better, but `Jsonata.Net.Native` is
currently the more mature, consolidated .NET package of the two —
tracked as a live, unresolved question in `10-open-questions.md`, not
assumed away.

## Where this project uses it

`ADR-018`/`ADR-037` — the declarative half of upcast mapping, alongside
GraphQL SDL directives (`@renamedFrom`/`@derivedFrom`) as self-describing
mapping metadata. **Before building**: spike against 2–3 of the
candidates below with this project's actual upcast-mapping shapes, and
pick based on maintenance activity and API fit at that time, rather than
locking in a choice now that the search for this doc couldn't
confidently make.

## Candidates

- [Cel.NET](https://www.nuget.org/packages/Cel.NET/)
- [Cel](https://www.nuget.org/packages/Cel) (telus-labs)
- [cel-net](https://github.com/telus-labs/cel-net)
- [cel-csharp](https://github.com/rofrankel/cel-csharp) (ANTLR4-generated parser + evaluator)
- Spec: [cel.dev](https://cel.dev/) / [cel-spec](https://github.com/cel-expr/cel-spec)
