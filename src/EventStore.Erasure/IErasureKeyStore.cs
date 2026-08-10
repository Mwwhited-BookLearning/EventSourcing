namespace EventStore.Erasure;

// One Data-Encryption Key (DEK) per (AppId, EntityId), generated the first
// time a classified field is published for that entity (ADR-057).
// Envelope-encryption shape: a backend never hands raw key material to the
// caller -- matching Vault's own transit-engine API, and every real KMS
// (Key Vault/KMS/Vault all expose "encrypt under this key handle," never
// "give me the key"). Callers only ever see an opaque key reference plus
// Create/Encrypt/Decrypt/Destroy.
public interface IErasureKeyStore
{
    // Idempotent per (appId, entityId) is the CALLER's responsibility
    // (ErasureKeyService checks EntityErasureKey first) -- this always
    // creates a fresh key and returns its new reference.
    Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default);

    Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default);

    // null return means the key has been destroyed -- callers must treat
    // this as "erased," never as a transient failure to retry.
    Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default);

    // Irreversible -- the whole point of crypto-shredding (ADR-057).
    Task DestroyKeyAsync(string keyReference, CancellationToken ct = default);
}
