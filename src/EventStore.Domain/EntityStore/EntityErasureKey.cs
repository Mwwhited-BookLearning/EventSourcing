namespace EventStore.Domain.EntityStore;

// Shape is the data-model authority: docs/data/entity-store.md.
// Wrapped-DEK METADATA only, never the key material itself, which lives
// only in the configured IErasureKeyStore backend (ADR-057). A critical
// authoritative store per ADR-056 -- losing this row (independent of the
// external key store) loses the mapping needed to request an erasure for
// an already-existing entity without consulting that backend's own
// listing directly.
public class EntityErasureKey
{
    public string EntityId { get; set; } = default!;
    public string KeyReference { get; set; } = default!;
    // Which registered IErasureKeyStore backend actually issued KeyReference
    // -- necessary because ADR-057 selects a backend per AppId via ordinary
    // configuration, which can change over time; a later decrypt must still
    // reach the SAME backend a key was originally created under, never
    // re-derive it from the entity's current AppId config. Not itself named
    // in docs/data/entity-store.md's original EntityErasureKey shape --
    // added this pass as a genuine, necessary correction, the same "found a
    // real gap while implementing, fixed in the same pass" precedent
    // DerivationCursor/DerivationHopCount already established for a
    // different item.
    public string BackendName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ErasedAt { get; set; }
}
