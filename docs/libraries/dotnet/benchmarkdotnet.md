[← Libraries index](../README.md)

# BenchmarkDotNet (dotnet)

**What it's for:** the de facto standard .NET micro-benchmarking
library — runs a benchmark method multiple times in an isolated
process, controlling for JIT warm-up/GC noise, and reports statistically
sound timing with a structured comparison against a previous baseline
run. Purpose-built for exactly "did this change make the hot path
slower," not general load/throughput testing (that's `NBomber`'s job,
named as a future escalation, not adopted).

**Why bought, not built:** correctly isolating a benchmark from JIT
warm-up, GC pauses, and OS scheduling noise is a solved, non-trivial
measurement problem with no project-specific value in reimplementing.

## General usage

```csharp
[MemoryDiagnoser]
public class FoldStepBenchmarks
{
    [Benchmark(Baseline = true)]
    public JsonObject MergePatch() => EntityDataMerger.MergePatch(_current, _patch);

    [Benchmark]
    public string ComputeChainHash() => EventChainHash.Compute(EventChainHash.Genesis, _payloadHash, 12345L);
}
```

```
dotnet run -c Release --project src/EventStore.Benchmarks
```

Run against a git ref (a tag, a previous commit's build) to get an
automatic regression comparison, not just an absolute number.

## Where this project uses it

`ADR-085` — `src/EventStore.Benchmarks/FoldStepBenchmarks.cs` benchmarks
the Router fold step's own pure-merge primitive
(`EntityDataMerger.MergePatch`) and `ADR-019`'s hash-chain computation
(`EventChainHash.Compute`); `JsonPathTranslationBenchmarks.cs` benchmarks
all three `IJsonPathTranslator` implementations' per-provider filter
translation. `RouterWorker.FoldAsync` itself is `internal` and EF-Core-
bound (a live `DbContext`, a stored `EventLog` row) — deliberately not
benchmarked directly, since its cost is dominated by I/O this suite isn't
measuring; `MergePatch` is the actual CPU-bound hot path inside it.
Adopted now, deliberately ahead of any load/soak testing infrastructure,
since it needs no running deployment to be useful — a relative, not
absolute, regression check.

## Links

- [benchmarkdotnet.org](https://benchmarkdotnet.org/)
- [github.com/dotnet/BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet)
