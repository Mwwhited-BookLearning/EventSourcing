using System.Collections.Concurrent;
using System.Text.Json;
using Zeebe.Client;

// Real BPMN 2.0 process definition, a standalone standard XML file, never
// an inline C# string literal, per direct request.
var bpmnPath = Path.Combine(AppContext.BaseDirectory, "Bpmn", "AdverseEventReview.bpmn");

// Needs a real Zeebe broker already running -- see this spike's own
// README.md for the docker run command. No embedded/local dev-server
// equivalent exists for Zeebe the way TemporalSpike has one for Temporal.
var client = ZeebeClient.Builder()
    .UseGatewayAddress("127.0.0.1:26500")
    .UsePlainText()
    .Build();

await client.NewDeployCommand().AddResourceFile(bpmnPath).Send();

var completions = new ConcurrentDictionary<string, TaskCompletionSource>();
using var cts = new CancellationTokenSource();

// client.NewWorker()...Open() never activated a single job in this spike --
// confirmed via a throwaway manual NewActivateJobsCommand diagnostic that
// jobs genuinely existed and were activatable the whole time, so the gap is
// specific to the high-level worker builder against this broker/client
// combination, not the broker or the process definition. Falls back to
// hand-rolling the same activate/complete loop NewWorker wraps internally --
// still real, idiomatic Zeebe client code (NewActivateJobsCommand/
// NewCompleteJobCommand are the SDK's own documented primitives), just
// without the higher-level convenience wrapper. See this spike's own
// README.md for the full account.
async Task PollJobTypeAsync(string jobType, string logLine, bool isTerminal, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var response = await client.NewActivateJobsCommand()
            .JobType(jobType)
            .MaxJobsToActivate(5)
            .WorkerName($"{jobType}-worker")
            .Timeout(TimeSpan.FromSeconds(10))
            .Send();

        foreach (var job in response.Jobs)
        {
            Console.WriteLine(logLine);
            await client.NewCompleteJobCommand(job.Key).Send();
            if (isTerminal)
            {
                var vars = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.Variables)!;
                var entityId = vars["entityId"].GetString()!;
                completions.GetOrAdd(entityId, _ => new TaskCompletionSource()).TrySetResult();
            }
        }

        if (response.Jobs.Count == 0)
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
    }
}

var jobTypes = new (string Type, string LogLine, bool IsTerminal)[]
{
    ("publish-event", "  -> POST /publish/AdverseEventReported", false),
    ("set-status-pending", "  -> AuthorityStatus set to pending_review (ADR-035/042)", false),
    ("delegate-access", "  -> UCAN delegation issued, scoped to this entity (ADR-043)", false),
    ("colleague-review", "  -> Colleague reads the pending finding via the delegated grant", false),
    ("request-decision", "  -> POST /publish/authorityDecision (RFC 9470 step-up enforced, ADR-066)", false),
    ("fold-now", "  -> Entity Store folds the finding now (accepted)", true),
    ("leave-untouched", "  -> Entity Store left untouched (rejected)", true),
    ("fold-immediate", "  -> Entity Store folds immediately, Full (ordinary, non-serious publish)", true),
};
var pollTasks = jobTypes.Select(jt => PollJobTypeAsync(jt.Type, jt.LogLine, jt.IsTerminal, cts.Token)).ToArray();

Console.WriteLine("=== Scenario 1: SeriousAdverseEvent = true, PI decision = accepted ===");
await RunAsync(client, completions, "ae-1", severityScore: 8, eventType: "Respiratory", authorityDecision: true);

Console.WriteLine();
Console.WriteLine("=== Scenario 2: SeriousAdverseEvent = true, PI decision = rejected ===");
await RunAsync(client, completions, "ae-2", severityScore: 8, eventType: "Respiratory", authorityDecision: false);

Console.WriteLine();
Console.WriteLine("=== Scenario 3: SeriousAdverseEvent = false (ordinary publish) ===");
await RunAsync(client, completions, "ae-3", severityScore: 2, eventType: "Respiratory", authorityDecision: null);

// The in-flight long-poll ActivateJobsCommand call surfaces a cancelled
// CancellationTokenSource as Grpc.Core.RpcException(DeadlineExceeded), not
// OperationCanceledException -- a real, worth-noting shutdown-path quirk
// found only by actually cancelling a live poll, not a functional problem
// (every scenario above already completed correctly before this runs).
cts.Cancel();
try { await Task.WhenAll(pollTasks); }
catch (Exception ex) when (ex is OperationCanceledException or Grpc.Core.RpcException) { }

static async Task RunAsync(IZeebeClient client, ConcurrentDictionary<string, TaskCompletionSource> completions, string entityId, int severityScore, string eventType, bool? authorityDecision)
{
    var tcs = completions.GetOrAdd(entityId, _ => new TaskCompletionSource());

    await client.NewCreateProcessInstanceCommand()
        .BpmnProcessId("AdverseEventReview")
        .LatestVersion()
        .Variables(JsonSerializer.Serialize(new { entityId, severityScore, eventType }))
        .Send();

    if (authorityDecision is { } accepted)
    {
        // Real message correlation, not simulated -- Zeebe buffers the
        // message for its time-to-live even if published slightly before
        // the process instance reaches the catch event, so no artificial
        // delay is needed before this call.
        await client.NewPublishMessageCommand()
            .MessageName("AuthorityDecisionPublished")
            .CorrelationKey(entityId)
            .Variables(JsonSerializer.Serialize(new { accepted }))
            .TimeToLive(TimeSpan.FromSeconds(10))
            .Send();
    }

    await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
}
