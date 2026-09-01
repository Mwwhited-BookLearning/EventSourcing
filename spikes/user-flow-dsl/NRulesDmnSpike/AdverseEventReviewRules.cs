using NRules.Fluent.Dsl;

namespace NRulesDmnSpike;

// Each rule below fires once its own preconditions (facts already present)
// are satisfied, forward-chaining through the same eight steps the other
// spikes in this folder walk as an AST -- here there is no AST at all, only
// accumulating facts and pattern-matched rules. The chain naturally pauses
// after ColleagueReviewRule/PublishDecisionRequestRule: nothing further can
// fire until Program.cs inserts the external AuthorityDecisionPublished fact
// (the human PI decision), the same "wait for outside input" role Elsa's own
// spike needed a purpose-built blocking/bookmark activity to fill.

public sealed class ClassifyEventRule : Rule
{
    public override void Define()
    {
        AdverseEventReported reported = null!;
        IAdverseEventClassifier classifier = null!;

        Dependency()
            .Resolve(() => classifier);

        When()
            .Match(() => reported)
            .Not<Classified>(c => c.EntityId == reported.EntityId);

        Then()
            .Do(ctx => ctx.Insert(new Classified(reported.EntityId, classifier.Classify(reported.SeverityScore, reported.EventType))));
    }
}

public sealed class RouteToSecondaryReviewRule : Rule
{
    public override void Define()
    {
        Classified classified = null!;

        When()
            .Match(() => classified, c => c.ReviewPath == "SecondaryReview")
            .Not<AuthorityStatusSet>(s => s.EntityId == classified.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> AuthorityStatus set to pending_review (ADR-035/042)"))
            .Do(ctx => ctx.Insert(new AuthorityStatusSet(classified.EntityId, "pending_review")));
    }
}

public sealed class DelegateAccessRule : Rule
{
    public override void Define()
    {
        AuthorityStatusSet status = null!;

        When()
            .Match(() => status, s => s.Status == "pending_review")
            .Not<DelegationIssued>(d => d.EntityId == status.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> UCAN delegation issued, scoped to this entity (ADR-043)"))
            .Do(ctx => ctx.Insert(new DelegationIssued(status.EntityId)));
    }
}

public sealed class ColleagueReviewRule : Rule
{
    public override void Define()
    {
        DelegationIssued delegation = null!;

        When()
            .Match(() => delegation)
            .Not<ColleagueReviewed>(c => c.EntityId == delegation.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> Colleague reads the pending finding via the delegated grant"))
            .Do(ctx => ctx.Insert(new ColleagueReviewed(delegation.EntityId)));
    }
}

public sealed class PublishDecisionRequestRule : Rule
{
    public override void Define()
    {
        ColleagueReviewed reviewed = null!;

        When()
            .Match(() => reviewed)
            .Not<AuthorityDecisionRequested>(r => r.EntityId == reviewed.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> POST /publish/authorityDecision (RFC 9470 step-up enforced, ADR-066)"))
            .Do(ctx => ctx.Insert(new AuthorityDecisionRequested(reviewed.EntityId)));
    }
}

public sealed class FoldOnAcceptRule : Rule
{
    public override void Define()
    {
        AuthorityDecisionPublished decision = null!;

        When()
            .Match(() => decision, d => d.Accepted)
            .Not<Folded>(f => f.EntityId == decision.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> Entity Store folds the finding now (accepted)"))
            .Do(ctx => ctx.Insert(new Folded(decision.EntityId, "CatchUp")));
    }
}

public sealed class LeaveUntouchedOnRejectRule : Rule
{
    public override void Define()
    {
        AuthorityDecisionPublished decision = null!;

        When()
            .Match(() => decision, d => !d.Accepted)
            .Not<Folded>(f => f.EntityId == decision.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> Entity Store left untouched (rejected)"))
            .Do(ctx => ctx.Insert(new Folded(decision.EntityId, "Untouched")));
    }
}

public sealed class FoldImmediateRule : Rule
{
    public override void Define()
    {
        Classified classified = null!;

        When()
            .Match(() => classified, c => c.ReviewPath == "ImmediateFold")
            .Not<Folded>(f => f.EntityId == classified.EntityId);

        Then()
            .Do(ctx => Console.WriteLine("  -> Entity Store folds immediately, Full (ordinary, non-serious publish)"))
            .Do(ctx => ctx.Insert(new Folded(classified.EntityId, "Full")));
    }
}
