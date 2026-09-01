namespace NRulesDmnSpike;

public sealed record AdverseEventReported(string EntityId, int SeverityScore, string EventType);
public sealed record Classified(string EntityId, string ReviewPath);
public sealed record AuthorityStatusSet(string EntityId, string Status);
public sealed record DelegationIssued(string EntityId);
public sealed record ColleagueReviewed(string EntityId);
public sealed record AuthorityDecisionRequested(string EntityId);
public sealed record AuthorityDecisionPublished(string EntityId, bool Accepted);
public sealed record Folded(string EntityId, string Mode);
