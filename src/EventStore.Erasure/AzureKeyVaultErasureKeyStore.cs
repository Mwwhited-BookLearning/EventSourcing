using System.Security.Cryptography;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace EventStore.Erasure;

// The Azure cloud tier (ADR-057) -- real Azure.Security.KeyVault.Keys,
// verified against the actual installed package before writing this
// (same "don't trust the library doc's own usage snippet verbatim"
// discipline HashiCorpVaultErasureKeyStore's own header comment already
// established -- that one named a method that didn't actually exist).
//
// Envelope encryption, not direct per-value Key Vault calls: Key Vault's
// own RSA-OAEP encrypt/decrypt is size-limited (well under a real field
// value's possible length) and, critically, NOT deterministic -- OAEP
// padding includes a random seed, so encrypting the same plaintext twice
// produces different ciphertext, which would break ADR-011's publish-
// idempotency comparison the exact same way a naive random-nonce local
// AES scheme would (LocalErasureKeyStore's/HashiCorpVaultErasureKeyStore's
// own comments already explain why that's unacceptable). Real KMS usage
// resolves this the standard way: CreateKeyAsync generates a local
// AES-256 Data-Encryption Key, wraps (encrypts) it ONCE via Key Vault's
// own WrapKey (small, fixed-size, safe under RSA-OAEP's limit), and
// returns "{keyName}:{base64WrappedDek}" as the opaque keyReference --
// no local persistence of our own needed, since ADR-057's own "wrapped-
// key metadata only" framing already means the CALLER persists whatever
// reference this returns (EntityErasureKey.KeyReference). Encrypt/Decrypt
// unwrap the DEK via Key Vault, then use EnvelopeAesGcm's OWN deterministic-
// nonce AES-256-GCM locally -- the same primitive Local/Vault already use,
// so idempotency holds identically across every backend.
public class AzureKeyVaultErasureKeyStore(KeyClient keyClient) : IErasureKeyStore
{
    private static readonly KeyWrapAlgorithm WrapAlgorithm = KeyWrapAlgorithm.RsaOaep256;

    public async Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default)
    {
        var keyName = ToKeyName(appId, entityId);
        await keyClient.CreateRsaKeyAsync(new CreateRsaKeyOptions(keyName), ct);

        var dek = RandomNumberGenerator.GetBytes(32); // AES-256
        var cryptoClient = keyClient.GetCryptographyClient(keyName);
        var wrapped = await cryptoClient.WrapKeyAsync(WrapAlgorithm, dek, ct);
        return ToReference(keyName, wrapped.EncryptedKey);
    }

    public async Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct)
            ?? throw new InvalidOperationException($"Azure Key Vault erasure key '{keyReference}' does not exist or has been destroyed");
        return EnvelopeAesGcm.Encrypt(dek, plaintext);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct);
        return dek is null ? null : EnvelopeAesGcm.Decrypt(dek, ciphertext);
    }

    public async Task DestroyKeyAsync(string keyReference, CancellationToken ct = default)
    {
        var (keyName, _) = FromReference(keyReference);
        // Delete then Purge -- ADR-057's "irreversible" requirement means a
        // soft-deleted (recoverable) key alone isn't enough; Key Vault's own
        // soft-delete protection has to be explicitly overridden via Purge.
        var deleteOperation = await keyClient.StartDeleteKeyAsync(keyName, ct);
        await deleteOperation.WaitForCompletionAsync(ct);
        await keyClient.PurgeDeletedKeyAsync(keyName, ct);
    }

    private async Task<byte[]?> UnwrapDekAsync(string keyReference, CancellationToken ct)
    {
        var (keyName, wrappedDek) = FromReference(keyReference);
        try
        {
            var cryptoClient = keyClient.GetCryptographyClient(keyName);
            var unwrapped = await cryptoClient.UnwrapKeyAsync(WrapAlgorithm, wrappedDek, ct);
            return unwrapped.Key;
        }
        catch (RequestFailedException)
        {
            // The wrapping key (or key vault entry) no longer resolves --
            // ADR-057's own contract for this method is "null means
            // erased," never a transient error to bubble up, the same
            // posture HashiCorpVaultErasureKeyStore's own catch already
            // establishes for a different backend.
            return null;
        }
    }

    private static string ToKeyName(string appId, string entityId) => $"dek-{appId}-{entityId}".Replace(':', '-');

    private static string ToReference(string keyName, byte[] wrappedDek) => $"{keyName}:{Convert.ToBase64String(wrappedDek)}";

    private static (string KeyName, byte[] WrappedDek) FromReference(string keyReference)
    {
        var separatorIndex = keyReference.IndexOf(':');
        return (keyReference[..separatorIndex], Convert.FromBase64String(keyReference[(separatorIndex + 1)..]));
    }
}
