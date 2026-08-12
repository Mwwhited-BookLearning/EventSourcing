[← Libraries index](../README.md)

# Jsonata.Net.Native (dotnet)

**What it's for:** the .NET port of JSONata — a JSON query/transform
language — validated against the official JSONata JS test suite for
spec conformance, zero external dependencies.

**Why it's here:** not this design's default (see [`CEL`](cel-dotnet.md)
and [`ADR-053`](../../adrs/adr-053-pluggable-upcast-expression-engine.md)),
but a real, supported alternative implementation of
`IUpcastExpressionEvaluator` — a deployment swaps to this instead of CEL
specifically for array-aggregation upcast mappings (`$sum()`,
sequence-flattening) CEL has no native equivalent for, or simply because
this package is currently the more consolidated, actively-maintained
.NET implementation of the two languages. **A real deployment-time
configuration switch, corrected from an earlier code-level-edit-only
gap**: `EventStore.Upcasting`'s
`UpcastingServiceCollectionExtensions.AddUpcasting(IConfiguration)` reads
`Upcasting:Engine` (`"Jsonata"`, case-insensitive, to opt in; unset or
anything else keeps the CEL default) and registers exactly this class
when configured — no rebuild, no composition-root code edit, matching
`ADR-053`'s own "via configuration, no core-engine change" framing.

## General usage

```csharp
var expr = Jsonata.Net.Native.JsonataQuery.Parse("FirstName & \" \" & LastName");
var result = expr.Eval(inputJsonNode);
```

Array aggregation, CEL's real gap:

```csharp
var expr = Jsonata.Net.Native.JsonataQuery.Parse("$sum(LineItems.Amount)");
```

## Where this project uses it

`ADR-053` — a documented, supported alternative to CEL for the
declarative half of upcast mapping (`ADR-018`/`ADR-037`), selected per
deployment via the composition root, never mixed with CEL within one
deployment.

## Links

- [nuget.org/packages/Jsonata.Net.Native](https://www.nuget.org/packages/Jsonata.Net.Native/)
- [docs.jsonata.org](https://docs.jsonata.org/overview.html) (the language spec this port implements)
