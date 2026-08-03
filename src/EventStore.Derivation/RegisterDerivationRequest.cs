namespace EventStore.Derivation;

// ADR-007's own prose gives the registration shape as query-string params
// on POST /create/{event-type} ($from=A,B&$on=...&$select=...), but this
// codebase already adapted that same OData-inspired style into a request
// body once before, for the same reason ($filter/etc. can carry PII/PHI --
// see EventStore.Follow.Api.FollowRequest, ADR-012) -- followed here too,
// for consistency, rather than parsing literal query-string operators.
public record RegisterDerivationRequest(
    string AppId,
    List<string> From,
    string On,
    string Select,
    string JoinTriggerMode,
    string BackfillMode,
    bool BackfillThroughDerivedSources,
    int? PendingJoinTtlSeconds,
    int? MaxHopCount);
