namespace EventStore.Lineage.Api;

// ADR-012 -- QUERY carries its arguments in the body, both optional; omitting
// both returns everything (docs/08-build-plan.md, "Lineage API").
public record LineageQueryRequest(int? Top, int? Skip);
