using Temporalio.Common;
using Temporalio.Workflows;

namespace TemporalSpike;

[Workflow]
public sealed class AdverseEventReviewWorkflow
{
    private bool? _authorityAccepted;

    [WorkflowRun]
    public async Task RunAsync(AdverseEventInput input)
    {
        var options = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) };

        await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.PublishAdverseEventReportedAsync(), options);

        var seriousAdverseEvent = input.SeverityScore >= 7 || input.EventType == "Cardiac";
        if (seriousAdverseEvent)
        {
            await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.SetAuthorityStatusPendingAsync(), options);
            await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.DelegateSecondaryOpinionAccessAsync(), options);
            await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.ColleagueReviewAsync(), options);
            await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.RequestAuthorityDecisionAsync(), options);

            // Temporal's own durable wait primitive -- survives a worker
            // crash/restart mid-wait because the workflow's history (not
            // in-memory state) is what's replayed, unlike this document's
            // other options where "pause for human input" has to be
            // engineered explicitly (Elsa's bookmark) or falls out of the
            // execution model for free (NRules' forward chaining).
            await Workflow.WaitConditionAsync(() => _authorityAccepted.HasValue);

            if (_authorityAccepted!.Value)
                await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.FoldNowAsync(), options);
            else
                await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.LeaveUntouchedAsync(), options);
        }
        else
        {
            await Workflow.ExecuteActivityAsync((AdverseEventActivities a) => a.FoldImmediateAsync(), options);
        }
    }

    [WorkflowSignal]
    public Task PublishAuthorityDecisionAsync(bool accepted)
    {
        _authorityAccepted = accepted;
        return Task.CompletedTask;
    }
}
