namespace EventStore.Persistence;

// Transitive ancestor/descendant traversal needs a native recursive CTE per
// provider (docs/06-solution-structure.md, "Event lineage (parent/child DAG)
// queries") -- EF Core's LINQ provider has no translation for these. Cycle
// safety (ADR-005) is mandatory regardless of ParentValidationMode: a
// Permissive-mode cycle can be reachable even starting from a Strict-mode
// event, so every implementation tracks a visited-path and caps traversal
// depth as a belt-and-suspenders limit, rather than trusting acyclicity.
// Three implementations live centrally here (like IJsonPathTranslator) --
// they need only provider-specific SQL text, not a provider-specific
// ADO.NET package reference, unlike IUniqueConstraintViolationDetector.
public interface IEventLineageQueryProvider
{
    Task<IReadOnlyList<Guid>> GetAncestorEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDescendantEventIdsAsync(EventStoreContext db, Guid rootEventId, CancellationToken ct = default);
}
