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
        return EnvelopeAesGcm.Encrypt(key, plaintext);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        var key = await GetActiveKeyAsync(keyReference, ct);
        return key is null ? null : EnvelopeAesGcm.Decrypt(key, ciphertext);
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
}
