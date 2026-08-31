using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime;
using ElsaSpike;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddElsa();
var provider = services.BuildServiceProvider();
var runner = provider.GetRequiredService<IWorkflowRunner>();

await RunScenarioAsync(runner, seriousAdverseEvent: true, accepted: true, label: "Scenario 1: SeriousAdverseEvent = true, PI decision = accepted");
await RunScenarioAsync(runner, seriousAdverseEvent: true, accepted: false, label: "Scenario 2: SeriousAdverseEvent = true, PI decision = rejected");
await RunScenarioAsync(runner, seriousAdverseEvent: false, accepted: false, label: "Scenario 3: SeriousAdverseEvent = false (ordinary publish)");

static async Task RunScenarioAsync(IWorkflowRunner runner, bool seriousAdverseEvent, bool accepted, string label)
{
    Console.WriteLine();
    Console.WriteLine($"=== {label} ===");

    // Same shape as the PlantUML-native spike's own worked example --
    // Sequence/If mirror the diagram's own start/if/endif/stop structure,
    // one real Elsa activity per diagram action. acceptedVariable (a real
    // workflow-level Variable<bool>, not an Output<bool> -- see
    // WaitForAuthorityDecisionActivity's own comment for why) carries the
    // PI's resumed decision into the second If's Condition.
    var acceptedVariable = new Variable<bool>();

    var workflow = new Sequence
    {
        Variables = { acceptedVariable },
        Activities =
        {
            new WriteLine("  -> POST /publish/AdverseEventReported"),
            new If
            {
                Condition = new Input<bool>(seriousAdverseEvent),
                Then = new Sequence
                {
                    Activities =
                    {
                        new WriteLine("  -> AuthorityStatus set to pending_review (ADR-035/042)"),
                        new WriteLine("  -> UCAN delegation issued, scoped to this entity (ADR-043)"),
                        new WriteLine("  -> Colleague reads the pending finding via the delegated grant"),
                        new WaitForAuthorityDecisionActivity(acceptedVariable),
                        new If
                        {
                            // The paused/resumed decision, flowing through a real workflow
                            // Variable<bool> -- proving control flow genuinely resumes
                            // correctly, not just that a log line printed.
                            Condition = new Input<bool>(acceptedVariable),
                            Then = new WriteLine("  -> Entity Store folds the finding now (accepted)"),
                            Else = new WriteLine("  -> Entity Store left untouched (rejected)"),
                        },
                    },
                },
                Else = new WriteLine("  -> Entity Store folds immediately, Full (ordinary, non-serious publish)"),
            },
        },
    };

    var firstRun = await runner.RunAsync(workflow, new RunWorkflowOptions());

    if (firstRun.WorkflowState.Status == Elsa.Workflows.WorkflowStatus.Running)
    {
        // A real bookmark exists -- the workflow genuinely paused, exactly
        // like a real PI's authorityDecision publish hasn't happened yet.
        // Found only by actually running this, not assumed: RunAsync(workflow,
        // workflowState, options) alone re-executes the whole graph from the
        // start rather than continuing from the paused activity -- the real
        // resume API needs BookmarkId set explicitly, naming exactly which
        // bookmark to resume at.
        var bookmarkId = firstRun.WorkflowState.Bookmarks.Single().Id;
        Console.WriteLine($"  (workflow paused on bookmark {bookmarkId} -- resuming with the PI's real decision)");
        await runner.RunAsync(firstRun.Workflow, firstRun.WorkflowState, new RunWorkflowOptions
        {
            BookmarkId = bookmarkId,
            Input = new Dictionary<string, object> { ["accepted"] = accepted },
        });
    }
}
