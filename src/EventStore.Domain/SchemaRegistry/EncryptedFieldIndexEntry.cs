namespace EventStore.Domain.SchemaRegistry;

// ADR-096/ADR-097 -- a derived, rebuildable structure, deliberately outside
// both ADR-019's ChainHash and ADR-033's Merkle-tree replication-sync
// (both computed only over StoredEvent/Payload) -- the same category as an
// ordinary CQRS read model, never the source of truth. Token is a keyed
// HMAC (Equality/Range) or ORE ciphertext (OrderRevealing) -- indexed,
// compared, never decrypted to search. Shared-scope rows for an erased
// entity are explicitly deleted by EntityErasureResolver's erasure
// side-effect (a real delete of this derived structure, not cryptographic
// destruction); PerEntity-scope rows need no separate delete step, since
// their Token becomes permanently uncomputable once the owning entity's
// DEK is destroyed.
public class EncryptedFieldIndexEntry
{
    public long Id { get; set; }
    public string AppId { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public string EventTypeName { get; set; } = default!;
    public string FieldJsonPath { get; set; } = default!;
    public SearchableIndexKind IndexKind { get; set; }
    public string? Granularity { get; set; } // Range only -- which bucketGranularities entry this row's Token is computed at; null for Equality/OrderRevealing
    public string Token { get; set; } = default!; // base64 HMAC (Equality/Range) or base64 ORE ciphertext (OrderRevealing) -- a string column, not byte[] (matches this codebase's existing base64-ciphertext convention, portable across all three providers as an ordinary indexed text column)
    public long StoredEventSequenceNumber { get; set; } // FK -> StoredEvent.SequenceNumber, the event this token was computed from
}
