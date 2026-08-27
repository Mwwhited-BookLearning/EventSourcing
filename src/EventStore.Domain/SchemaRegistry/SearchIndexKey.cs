namespace EventStore.Domain.SchemaRegistry;

// ADR-096 -- provider-agnostic metadata for a Shared-scope searchable-index
// HMAC key, one per (AppId, EventTypeName, FieldJsonPath) -- the same
// "metadata row here, key material only in the configured backend" split
// EntityErasureKey/IErasureKeyStore already establish for crypto-shredding
// DEKs. Distinct lifecycle from EntityErasureKey: never auto-destroyed on
// entity erasure -- manual rotation only, a real, named, accepted gap
// (ADR-096's own Consequences).
public class SearchIndexKey
{
    public string AppId { get; set; } = default!;
    public string EventTypeName { get; set; } = default!;
    public string FieldJsonPath { get; set; } = default!;
    public string KeyReference { get; set; } = default!;
    public string BackendName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}

// The concrete backing store for the "local" ISearchIndexKeyStore backend --
// same shape as LocalErasureKeyMaterial, but a raw symmetric key held
// directly (not envelope-wrapped): unlike a DEK, this key is never used to
// encrypt data at rest, only to compute an HMAC identically at publish and
// query time, so there's no confidentiality reason to hide it behind an
// envelope-encrypt/decrypt indirection the way IErasureKeyStore does.
public class LocalSearchIndexKeyMaterial
{
    public string KeyReference { get; set; } = default!; // PK, opaque, matches SearchIndexKey.KeyReference for keys using this backend
    public byte[] Key { get; set; } = default!;
}
