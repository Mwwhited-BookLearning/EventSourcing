namespace EventStore.Domain.EntityStore;

// The concrete backing store for the "local" IErasureKeyStore backend
// (ADR-057 names this tier as "a simple encrypted file/DB-backed store for
// dev" without specifying a shape) -- this pass's own answer, documented
// in docs/data/entity-store.md per this repo's "the ADR that adds a
// persisted shape is that shape's authority" rule. Never confused with
// EntityErasureKey above: that row is provider-agnostic metadata every
// backend shares (an opaque KeyReference); this table is ONLY the local
// backend's own private storage for what that reference actually points
// to -- a Vault-backed deployment has no row here at all.
public class LocalErasureKeyMaterial
{
    public string KeyReference { get; set; } = default!; // PK, opaque, matches EntityErasureKey.KeyReference for entities using this backend
    public byte[]? WrappedKey { get; set; }                // AES-256 key bytes; null once destroyed -- irreversible, not a soft flag alone
    public bool Destroyed { get; set; }
}
