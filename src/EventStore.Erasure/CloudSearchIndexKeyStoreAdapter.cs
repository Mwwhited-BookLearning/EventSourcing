using System.Security.Cryptography;
using EventStore.Abstractions;

namespace EventStore.Erasure;

// ADR-096 -- wraps ANY IErasureKeyStore-shaped backend (Azure Key Vault,
// AWS KMS, Google Cloud KMS, HashiCorp Vault) into an ISearchIndexKeyStore,
// rather than reimplementing four cloud SDKs a second time for a
// differently-shaped interface. Every existing cloud IErasureKeyStore
// already exposes exactly the two primitives a search-index key actually
// needs: CreateKeyAsync (create a cloud-managed key, return an opaque
// reference) and EncryptAsync (a deterministic operation under that key).
//
// ComputeHmacAsync reuses the SAME key-derivation trick PayloadIndexer's
// PerEntity scope already uses for the identical reason (IErasureKeyStore
// never exposes raw key material, by design, mirroring a real KMS):
// encrypt a fixed label under the cloud-managed key, hash the resulting
// ciphertext into a local, single-use derived key, then compute the
// caller's real HMAC with that. This works identically across every
// provider without needing a native HMAC/MAC API each one may or may not
// expose uniformly (AWS KMS's GenerateMac exists; Azure Key Vault and GCP
// KMS have no equivalent operation on a Key object) -- one mechanism,
// not four bespoke ones.
public class CloudSearchIndexKeyStoreAdapter(IErasureKeyStore backend) : ISearchIndexKeyStore
{
    private static readonly byte[] DerivationInfo = "search-index-hmac-v1"u8.ToArray();

    public Task<string> CreateKeyAsync(string appId, string fieldKey, CancellationToken ct = default) =>
        backend.CreateKeyAsync(appId, fieldKey, ct);

    public async Task<byte[]> ComputeHmacAsync(string keyReference, byte[] data, CancellationToken ct = default)
    {
        var derivationCiphertext = await backend.EncryptAsync(keyReference, DerivationInfo, ct);
        var derivedKey = SHA256.HashData(derivationCiphertext);
        return HMACSHA256.HashData(derivedKey, data);
    }
}
