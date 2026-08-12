using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using EventStore.Domain.EventLog;
using EventStore.Router;

namespace EventStore.Benchmarks;

// ADR-085 -- micro-benchmarks against the Router fold step's own pure-merge
// primitive (EntityDataMerger.MergePatch, the same target ADR-063's FsCheck
// properties exercise in tests/EventStore.UnitTests) and ADR-019's hash-chain
// computation. RouterWorker.FoldAsync itself is `internal` and EF-Core-bound
// (a live DbContext, a stored EventLog row) -- not a fair micro-benchmark
// target, since its cost is dominated by I/O this suite deliberately isn't
// measuring. These two pure, allocation-only functions are the actual CPU-
// bound hot path inside it.
[MemoryDiagnoser]
public class FoldStepBenchmarks
{
    private JsonObject _current = null!;
    private JsonObject _patch = null!;

    [GlobalSetup]
    public void Setup()
    {
        _current = new JsonObject
        {
            ["WidgetId"] = "widget-1",
            ["Name"] = "Original Name",
            ["Status"] = "Active",
            ["Quantity"] = 42,
            ["Owner"] = "team-a",
        };
        _patch = new JsonObject
        {
            ["Name"] = "Updated Name",
            ["Status"] = "Retired",
        };
    }

    [Benchmark(Baseline = true)]
    public JsonObject MergePatch() => EntityDataMerger.MergePatch(_current, _patch);

    [Benchmark]
    public string ComputeChainHash() => EventChainHash.Compute(EventChainHash.Genesis, "payload-hash-example", 12345L);
}
