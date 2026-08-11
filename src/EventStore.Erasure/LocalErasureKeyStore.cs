using System.Security.Cryptography;
using EventStore.Domain.EntityStore;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Erasure;

// The "local" tier ADR-057 names for dev and small/single-node deployments
// with no real KMS available -- a simple encrypted file/DB-backed store,
// per that ADR's own phrasing. Backed by LocalErasureKeyMaterial in the
// SAME EventStoreContext (a genuinely durable choice, not in-memory --
// losing key material would be equivalent to erasing every subject
// protected by it at once, per ADR-057's own Consequences on this store's
// criticality). AES-256-GCM, one random key per entity; DestroyKeyAsync
// clears the key bytes outright, not just a soft "destroyed" flag.
public class LocalErasureKeyStore(EventStoreContext db) : IErasureKeyStore
{
    public async Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default)
    {
        var keyReference = $"local:{Guid.NewGuid():N}";
        db.LocalErasureKeyMaterials.Add(new LocalErasureKeyMaterial
        {
            KeyReference = keyReference,
            WrappedKey = RandomNumberGenerator.GetBytes(32), // AES-256
            Destroyed = false,
        });
        await db.SaveChangesAsync(ct);
        return keyReference;
    }

    public async Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default)
    {
        var key = await GetActiveKeyAsync(keyReference, ct)
            ?? throw new InvalidOperationException($"local erasure key {keyReference} does not exist or has been destroyed");
        return AesGcmEncrypt(key, plaintext);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        var key = await GetActiveKeyAsync(keyReference, ct);
        return key is null ? null : AesGcmDecrypt(key, ciphertext);
    }

    public async Task DestroyKeyAsync(string keyReference, CancellationToken ct = default)
    {
        var material = await db.LocalErasureKeyMaterials.SingleOrDefaultAsync(m => m.KeyReference == keyReference, ct);
        if (material is null)
            return;
        material.WrappedKey = null; // irreversible -- not merely flagged
        material.Destroyed = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<byte[]?> GetActiveKeyAsync(string keyReference, CancellationToken ct)
    {
        var material = await db.LocalErasureKeyMaterials.AsNoTracking().SingleOrDefaultAsync(m => m.KeyReference == keyReference, ct);
        return material is { Destroyed: false, WrappedKey: not null } ? material.WrappedKey : null;
    }

    // nonce || tag || ciphertext, one self-contained blob per value. The
    // nonce is DERIVED from (key, plaintext) via HMAC, not random -- so the
    // same real value always produces the same ciphertext under the same
    // key ("convergent encryption," the same property Vault's own transit
    // engine offers via ConvergentEncryption for this identical need,
    // HashiCorpVaultErasureKeyStore's own comment explains further). This
    // is deliberate, not an oversight: ADR-011's publish idempotency
    // compares PayloadHash, computed over the STORED (post-encryption)
    // payload per ADR-057's own explicit text -- random-nonce encryption
    // would make retrying the identical logical publish (the same eventId,
    // the same real field values) hash differently every time and falsely
    // report a 409 Conflict instead of an idempotent replay. The accepted,
    // well-known cost of convergent encryption -- two ciphertexts reveal
    // whether they encrypt the same plaintext -- is not a concern for THIS
    // threat model (crypto-shredding's goal is making a value permanently
    // unrecoverable on erasure, not hiding equality between still-live
    // values).
    private static byte[] AesGcmEncrypt(byte[] key, byte[] plaintext)
    {
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var nonce = DeriveNonce(key, plaintext);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    private static byte[] DeriveNonce(byte[] key, byte[] plaintext) =>
        HMACSHA256.HashData(key, plaintext)[..AesGcm.NonceByteSizes.MaxSize];

    private static byte[] AesGcmDecrypt(byte[] key, byte[] blob)
    {
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var nonce = blob[..nonceSize];
        var tag = blob[nonceSize..(nonceSize + tagSize)];
        var ciphertext = blob[(nonceSize + tagSize)..];
        using var aes = new AesGcm(key, tagSize);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
