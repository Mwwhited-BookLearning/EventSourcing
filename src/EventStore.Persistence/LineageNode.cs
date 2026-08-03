namespace EventStore.Persistence;

// Per docs/03-api-contracts.md's Lineage API response shape (minus `restricted`,
// out of scope until "Event-Type Security"/claims exist). `Resolved: false` means
// this EventId was named as a parent (directly, or reached transitively) but never
// actually exists as a StoredEvent -- only possible under Permissive-mode
// ParentValidationMode (ADR-005).
public record LineageNode(Guid EventId, string? EventType, long? SequenceNumber, DateTimeOffset? OccurredAt, bool Resolved);
