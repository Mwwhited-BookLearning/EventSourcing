namespace EventStore.Abstractions;

// ADR-096 -- a Shared-scope searchable-index key computes a deterministic
// MAC over arbitrary future values (identically at publish time and query
// time), never encrypts data at rest -- a genuinely different operation
// from IErasureKeyStore's envelope encrypt/decrypt, so this is its own
// interface rather than a forced fit through that one. Mirrors real KMS
// "compute under a managed key, never hand back the key" primitives that
// exist for exactly this MAC use case (Vault Transit's /hmac endpoint, AWS
// KMS's GenerateMac) -- not a hypothetical shape invented for this design.
public interface ISearchIndexKeyStore
{
    // Idempotent per (appId, fieldKey) is the CALLER's responsibility
    // (SearchIndexKeyService checks SearchIndexKey first) -- this always
    // creates a fresh key and returns its new reference.
    Task<string> CreateKeyAsync(string appId, string fieldKey, CancellationToken ct = default);

    Task<byte[]> ComputeHmacAsync(string keyReference, byte[] data, CancellationToken ct = default);
}
