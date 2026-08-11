namespace EventStore.Persistence;

// Per docs/03-api-contracts.md's Lineage API response shape. `Resolved: false`
// means this EventId was named as a parent (directly, or reached transitively)
// but never actually exists as a StoredEvent -- only possible under
// Permissive-mode ParentValidationMode (ADR-005). `Restricted: true` (ADR-008,
// "Event-Type Security") means the node exists but the caller lacks the
// Read-direction RequiredClaims its EventType requires -- EventType/
// SequenceNumber/OccurredAt stay null for a restricted node the same way they
// already do for an unresolved one, matching that existing precedent, since
// this build stage's Lineage endpoint (like Follow's) does no further
// per-field JSON shaping.
public record LineageNode(Guid EventId, string? EventType, long? SequenceNumber, DateTimeOffset? OccurredAt, bool Resolved, bool Restricted = false);
