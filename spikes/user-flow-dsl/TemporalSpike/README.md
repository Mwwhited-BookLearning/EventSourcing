# Temporal spike — Option C

Proves the "durable execution platform" option from
[`docs/comparisons/user-flow-dsl.md`](../../../docs/comparisons/user-flow-dsl.md):
a real `[Workflow]` class with the same branching shape as the other
spikes in this folder, backed by a real Temporal Server run locally via
`Temporalio.Testing.WorkflowEnvironment.StartLocalAsync()` — **no manual
Docker or cluster setup**, unlike the Zeebe/Camunda 8 spike in this same
folder. The SDK lazily downloads the real server binary on first use and
runs it as a local subprocess for the lifetime of the spike.

Run with `dotnet run` from this directory. The first run downloads the
Temporal dev-server binary; subsequent runs reuse the cached copy.

## Shape

| File | Role |
|---|---|
| `AdverseEventActivities.cs` | Eight `[Activity]`-attributed methods — one per action in the scenario. Activities are Temporal's own designated place for side effects (I/O, logging); workflow code itself must stay deterministic and replay-safe, so nothing runs directly inside it. |
| `AdverseEventReviewWorkflow.cs` | A `[Workflow]` class with a `[WorkflowRun]` method containing the branching logic (mirrors every other spike's scenario exactly), and a `[WorkflowSignal]` method (`PublishAuthorityDecisionAsync`) the PI's real decision arrives through. |
| `Program.cs` | Starts the embedded dev server, a `TemporalWorker` polling its task queue, starts one workflow execution per scenario, and — for the two `SeriousAdverseEvent` scenarios — sends the signal to resume it. |

The workflow's own pause is `await Workflow.WaitConditionAsync(() =>
_authorityAccepted.HasValue)` — Temporal's durable wait primitive. Unlike
an ordinary in-memory `await`, this genuinely survives a worker process
crash: the workflow's *history*, not in-memory state, is what Temporal
replays to resume it. This spike doesn't kill the worker mid-wait to
prove that specifically (that would need a real, separate Temporal
Server rather than the embedded local one), but the mechanism is the
SDK's own real, documented one, not a spike-specific simulation.

## Findings

Worked end to end on the **first real run** — no API-mismatch friction
of the kind Option B's Elsa spike or Option E's DMN engine both hit, and
therefore no need for this spike's own reflection probe against the
installed assembly (`AntlrCustomDslSpike`/`NRulesDmnSpike`/`ElsaSpike`
all needed one; this one didn't). Every one of `[Workflow]`,
`[WorkflowRun]`, `[WorkflowSignal]`, `[Activity]`, `ActivityOptions`,
`Workflow.ExecuteActivityAsync`, `Workflow.WaitConditionAsync`,
`TemporalWorkerOptions.AddWorkflow`/`.AddAllActivities`,
`ITemporalClient.StartWorkflowAsync`, and `WorkflowHandle.SignalAsync`/
`.GetResultAsync` compiled and behaved exactly as expected on the first
try. This is a genuine, measured data point in the SDK's favor, the
mirror image of the Elsa README's own finding in this same folder.

The dev server's own startup output includes a handful of
`level=ERROR msg="Queue reader unable to retrieve tasks" ...
error="shard status unknown"` lines in its first few hundred
milliseconds, before shard initialization finishes. The workflow still
completes correctly — this is transient dev-server startup noise
emitted by Temporal's own Go binary, not something this spike's code
produces or controls, and not a real failure. Noted here only because
it's visible in the raw output below and worth not mistaking for one.
