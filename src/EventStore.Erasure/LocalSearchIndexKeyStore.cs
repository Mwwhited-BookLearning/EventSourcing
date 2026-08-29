using System.Security.Cryptography;
using EventStore.Abstractions;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Erasure;

// The "local" tier ADR-096 names for dev/small deployments, same posture
// as LocalErasureKeyStore. Backed by LocalSearchIndexKeyMaterial in the
// same EventStoreContext -- a genuinely durable choice, since this key's
// own loss would silently break every Shared-scope search for the fields
// it protects (a real, distinct-from-erasure availability risk, unrelated
// to crypto-shredding). Holds the raw HMAC key directly (see
// ISearchIndexKeyStore's own header comment for why that's the right shape
// here, unlike IErasureKeyStore's envelope model).
public class LocalSearchIndexKeyStore(EventStoreContext db) : ISearchIndexKeyStore
{
    public async Task<string> CreateKeyAsync(string appId, string fieldKey, CancellationToken ct = default)
    {
        var keyReference = $"local-search:{Guid.NewGuid():N}";
        db.LocalSearchIndexKeyMaterials.Add(new LocalSearchIndexKeyMaterial
        {
            KeyReference = keyReference,
            Key = RandomNumberGenerator.GetBytes(32), // HMAC-SHA256 key
        });
        await db.SaveChangesAsync(ct);
        return keyReference;
    }

    public async Task<byte[]> ComputeHmacAsync(string keyReference, byte[] data, CancellationToken ct = default)
    {
        var material = await db.LocalSearchIndexKeyMaterials.AsNoTracking().SingleOrDefaultAsync(m => m.KeyReference == keyReference, ct)
            ?? throw new InvalidOperationException($"local search-index key {keyReference} does not exist");
        return HMACSHA256.HashData(material.Key, data);
    }
}
