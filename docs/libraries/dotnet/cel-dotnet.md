[← Libraries index](../README.md)

# CEL for .NET (dotnet) — `Cel.NET` adopted

**What it's for:** Common Expression Language (CEL, Google) is a small,
non-Turing-complete expression language designed to evaluate untrusted
expressions quickly and safely — the declarative counterpart to
[Jint](jint.md)'s general-purpose JS sandbox, for the common upcast-
mapping case that's genuinely just field renames/simple derivations with
no real programming needed.

**Resolved this pass, per this doc's own "spike before building" note**
(build-plan item 11, "Hardening & Evolution," the first item that
actually needs a working evaluator): the .NET CEL ecosystem is still
fragmented — four differently-maintained community ports exist (`Cel.NET`,
`Cel`/telus-labs, `cel-net`, `cel-csharp`) — but two were actually spiked
(`Cel.NET` 1.1.0 and `Cel`/telus-labs 0.3.3, both installed from a real
NuGet restore, not assumed) rather than picked arbitrarily:
- **`Cel.NET`** — verified with a real compile-and-execute round trip
  (`ScriptHost.NewBuilder().Build().BuildScript(expr).WithDeclarations
  (Decls.NewVar("event", Decls.Dyn)).Build()`, then `script.Execute<T>
  (IDictionary<string, object>)`) against both a string-concatenation
  and an arithmetic expression shaped like a real upcast mapping — both
  produced the correct result with no ceremony beyond declaring the
  input as `Decls.Dyn`. Published history runs 2022 (`0.1.0`) through
  `1.1.0` (2026-07-02), reaching a stable `1.x` version, with continuous
  recent maintenance — the longest track record of the four candidates.
- **`Cel` (telus-labs)** — also currently maintained (`0.3.3`,
  2026-06-08) but still pre-`1.0`, suggesting a less settled API.
- `cel-net`/`cel-csharp` were not spiked — `Cel.NET` already cleared the
  bar this doc set ("pick based on maintenance activity and API fit"),
  so a fourth candidate added no further decision-relevant information.

**`Cel.NET` is the choice**: the longer, more active maintenance history
and the already-reached `1.x` stability line, confirmed against a real
working round trip, not assumed from package metadata alone.

**One honest caveat, stated rather than silently carried**: restoring
`Cel.NET` pulls in a `NU1904` critical-severity advisory
([GHSA-rxg9-xrhp-64gj](https://github.com/advisories/GHSA-rxg9-xrhp-64gj))
against `System.Drawing.Common` 4.7.0 — but only transitively through
`Antlr4BuildTasks` (`Cel.NET`'s own ANTLR-grammar code generator), a
**build-time-only** MSBuild task package (ships `.targets`/`.props`, no
runtime assembly) that never appears in a published application. The
same reasoning class this catalog already applies to [Jint](jint.md)'s
non-hermetic sandbox: named explicitly, judged not to reach this
project's actual runtime attack surface, not silently ignored.

## General usage (verified against `Cel.NET` 1.1.0)

```csharp
var host = ScriptHost.NewBuilder().Build();
var script = host.BuildScript("event.FirstName + ' ' + event.LastName")
    .WithDeclarations(Decls.NewVar("event", Decls.Dyn))
    .Build();

var result = script.Execute<string>(new Dictionary<string, object>
{
    ["event"] = new Dictionary<string, object> { ["FirstName"] = "A.", ["LastName"] = "Smith" },
});
```

**See [the CEL vs. JSONata comparison](../../comparisons/upcast-transform-language.md)**
for the full head-to-head — CEL fits this problem's shape better on
safety/performance/narrowness; `Jsonata.Net.Native` remains the
documented, supported alternative (`ADR-053`) for array-aggregation
upcast mappings CEL's macro set has no native answer for. That trade-off
is unaffected by resolving *which* CEL package backs the default —
`Jsonata.Net.Native`'s own single, consolidated .NET implementation was
never in question.

## Where this project uses it

`ADR-018`/`ADR-037` — the declarative half of upcast mapping (`Cel.main`
assembly, `Cel.Tools.ScriptHost`/`Cel.Checker.Decls`), alongside GraphQL
SDL directives (`@renamedFrom`/`@derivedFrom`) as self-describing mapping
metadata. Registered directly (not yet the keyed, swappable-via-
configuration seam — that ceremony, alongside `Jsonata.Net.Native` as a
registered alternative, is "Upcast Materialization + Downcast"'s own
scope, `ADR-053`). Implementation:
[`CelUpcastExpressionEvaluator.cs`](../../../src/EventStore.Upcasting/CelUpcastExpressionEvaluator.cs),
[`UpcastChain.cs`](../../../src/EventStore.Upcasting/UpcastChain.cs),
[`UpcastExpressionListParser.cs`](../../../src/EventStore.Upcasting/UpcastExpressionListParser.cs).

## Candidates considered

- [Cel.NET](https://www.nuget.org/packages/Cel.NET/) — **adopted**
- [Cel](https://www.nuget.org/packages/Cel) (telus-labs) — spiked, not chosen (pre-1.0, shorter track record)
- [cel-net](https://github.com/telus-labs/cel-net) — not spiked
- [cel-csharp](https://github.com/rofrankel/cel-csharp) (ANTLR4-generated parser + evaluator) — not spiked
- Spec: [cel.dev](https://cel.dev/) / [cel-spec](https://github.com/cel-expr/cel-spec)
