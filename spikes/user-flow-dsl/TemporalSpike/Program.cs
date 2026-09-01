using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using TemporalSpike;

// A real, lazily-downloaded Temporal dev server run as a local subprocess --
// no manual Docker/Temporal cluster setup, per Temporalio.Testing's own
// WorkflowEnvironment.StartLocalAsync design.
await using var env = await WorkflowEnvironment.StartLocalAsync();

const string taskQueue = "adverse-event-review-spike";
using var worker = new TemporalWorker(env.Client, new TemporalWorkerOptions(taskQueue)
    .AddWorkflow<AdverseEventReviewWorkflow>()
    .AddAllActivities(new AdverseEventActivities()));

using var cts = new CancellationTokenSource();
var workerTask = worker.ExecuteAsync(cts.Token);

Console.WriteLine("=== Scenario 1: SeriousAdverseEvent = true, PI decision = accepted ===");
await RunAsync(env.Client, taskQueue, "ae-1", severityScore: 8, eventType: "Respiratory", authorityDecision: true);

Console.WriteLine();
Console.WriteLine("=== Scenario 2: SeriousAdverseEvent = true, PI decision = rejected ===");
await RunAsync(env.Client, taskQueue, "ae-2", severityScore: 8, eventType: "Respiratory", authorityDecision: false);

Console.WriteLine();
Console.WriteLine("=== Scenario 3: SeriousAdverseEvent = false (ordinary publish) ===");
await RunAsync(env.Client, taskQueue, "ae-3", severityScore: 2, eventType: "Respiratory", authorityDecision: null);

cts.Cancel();
try { await workerTask; } catch (OperationCanceledException) { }

static async Task RunAsync(ITemporalClient client, string taskQueue, string workflowId, int severityScore, string eventType, bool? authorityDecision)
{
    var handle = await client.StartWorkflowAsync(
        (AdverseEventReviewWorkflow wf) => wf.RunAsync(new AdverseEventInput(severityScore, eventType)),
        new WorkflowOptions(id: workflowId, taskQueue: taskQueue));

    if (authorityDecision is { } accepted)
    {
        // Real durable-execution round trip: the workflow is genuinely
        // paused (WaitConditionAsync) inside the dev server's own history,
        // not just an in-process await, until this signal arrives.
        await handle.SignalAsync(wf => wf.PublishAuthorityDecisionAsync(accepted));
    }

    await handle.GetResultAsync();
}
