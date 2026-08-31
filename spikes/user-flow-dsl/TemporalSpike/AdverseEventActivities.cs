using Temporalio.Activities;

namespace TemporalSpike;

// Each activity is a real Temporal Activity -- the SDK's own designated
// place for non-deterministic side effects (I/O, logging), never done
// directly inside workflow code, docs/comparisons/user-flow-dsl.md Option C.
public sealed class AdverseEventActivities
{
    [Activity]
    public Task PublishAdverseEventReportedAsync()
    {
        Console.WriteLine("  -> POST /publish/AdverseEventReported");
        return Task.CompletedTask;
    }

    [Activity]
    public Task SetAuthorityStatusPendingAsync()
    {
        Console.WriteLine("  -> AuthorityStatus set to pending_review (ADR-035/042)");
        return Task.CompletedTask;
    }

    [Activity]
    public Task DelegateSecondaryOpinionAccessAsync()
    {
        Console.WriteLine("  -> UCAN delegation issued, scoped to this entity (ADR-043)");
        return Task.CompletedTask;
    }

    [Activity]
    public Task ColleagueReviewAsync()
    {
        Console.WriteLine("  -> Colleague reads the pending finding via the delegated grant");
        return Task.CompletedTask;
    }

    [Activity]
    public Task RequestAuthorityDecisionAsync()
    {
        Console.WriteLine("  -> POST /publish/authorityDecision (RFC 9470 step-up enforced, ADR-066)");
        return Task.CompletedTask;
    }

    [Activity]
    public Task FoldNowAsync()
    {
        Console.WriteLine("  -> Entity Store folds the finding now (accepted)");
        return Task.CompletedTask;
    }

    [Activity]
    public Task LeaveUntouchedAsync()
    {
        Console.WriteLine("  -> Entity Store left untouched (rejected)");
        return Task.CompletedTask;
    }

    [Activity]
    public Task FoldImmediateAsync()
    {
        Console.WriteLine("  -> Entity Store folds immediately, Full (ordinary, non-serious publish)");
        return Task.CompletedTask;
    }
}
