using Elsa.Workflows;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;

namespace ElsaSpike;

// The blocking-activity/bookmark shape docs.elsaworkflows.io's own
// "Blocking Activities & Triggers" page documents, verified directly
// against the REAL installed Elsa 3.7.1 assembly via reflection before
// writing this. Writes its resumed decision into a workflow-level
// Variable<bool>, not its own Output<bool> -- found only by actually
// running this: an Output-based Input<T> reference between two activity
// instances fails to rehydrate after a real bookmark-resume round trip
// ("Could not find a descriptor for expression type \"Output\""), while
// a Variable<T> (Elsa's own designed-for-persistence storage) survives
// it correctly.
public sealed class WaitForAuthorityDecisionActivity(Variable<bool> acceptedVariable) : Activity
{
    protected override void Execute(ActivityExecutionContext context)
    {
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = "WaitForAuthorityDecision",
            Callback = OnResumeAsync,
        });
    }

    private async ValueTask OnResumeAsync(ActivityExecutionContext context)
    {
        var accepted = context.WorkflowInput.TryGetValue("accepted", out var value) && value is true;
        context.Set(acceptedVariable, accepted);
        await context.CompleteActivityAsync();
    }
}
