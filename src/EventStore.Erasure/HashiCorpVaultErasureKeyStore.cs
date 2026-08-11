using System.Text;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.SecretsEngines;
using VaultSharp.V1.SecretsEngines.Transit;

namespace EventStore.Erasure;

// The on-prem/self-hosted tier (ADR-057) -- a real Vault transit-engine
// backend via VaultSharp, verified against its actual API surface (a
// throwaway reflection probe against the installed package, not the
// library doc's own usage snippet, which turned out to name a
// vaultClient.V1.System.DeleteTransitKeyAsync method that does not
// actually exist -- deletion is ITransitSecretsEngine.DeleteEncryptionKeyAsync
// instead; docs/libraries/dotnet/hashicorp-vault.md corrected in the same
// pass). Vault never hands raw key material back across any of these
// calls -- CipherText is Vault's own opaque "vault:v1:..." string, stored
// here as UTF8 bytes purely to fit IErasureKeyStore's byte[] shape.
public class HashiCorpVaultErasureKeyStore(IVaultClient vaultClient, string mountPoint = "transit") : IErasureKeyStore
{
    // A fixed, constant context for every Encrypt/Decrypt call -- convergence
    // is scoped to "same plaintext under this one key reference" (keyReference
    // is already entity-scoped), not per-context.
    private static readonly string FixedContext = Convert.ToBase64String("eventstore-erasure"u8.ToArray());

    public async Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default)
    {
        var keyName = ToKeyName(appId, entityId);
        // ConvergentEncryption -- same plaintext always produces the same
        // ciphertext under this key, Vault's own supported mechanism for
        // exactly the need LocalErasureKeyStore's own deterministic-nonce
        // comment explains in full: ADR-011's publish idempotency compares
        // PayloadHash computed over the STORED (post-encryption) payload,
        // so random-per-call ciphertext would make retrying an identical
        // logical publish falsely report a 409 Conflict.
        await vaultClient.V1.Secrets.Transit.CreateEncryptionKeyAsync(
            keyName, new CreateKeyRequestOptions { ConvergentEncryption = true, Derived = true }, mountPoint);
        return keyName;
    }

    public async Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default)
    {
        // Convergent+derived keys require an explicit context on every call
        // (Vault derives a per-context sub-key from it) -- a fixed, constant
        // context is deliberate: this store scopes convergence to "same
        // plaintext under this one key reference," not per-context, since
        // keyReference is already entity-scoped.
        var response = await vaultClient.V1.Secrets.Transit.EncryptAsync(
            keyReference,
            new EncryptRequestOptions { Base64EncodedPlainText = Convert.ToBase64String(plaintext), Base64EncodedContext = FixedContext },
            mountPoint);
        return Encoding.UTF8.GetBytes(response.Data.CipherText);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        try
        {
            var response = await vaultClient.V1.Secrets.Transit.DecryptAsync(
                keyReference,
                new DecryptRequestOptions { CipherText = Encoding.UTF8.GetString(ciphertext), Base64EncodedContext = FixedContext },
                mountPoint);
            return Convert.FromBase64String(response.Data.Base64EncodedPlainText);
        }
        catch (VaultApiException)
        {
            // The key (or the whole mount, if a caller destroyed it) no longer
            // resolves -- ADR-057's own contract for this method is "null means
            // erased," not "let the exception surface as a transient error."
            return null;
        }
    }

    public async Task DestroyKeyAsync(string keyReference, CancellationToken ct = default)
    {
        // Vault refuses deletion unless deletion_allowed is explicitly set on
        // the key first -- a deliberate safety default on Vault's own part,
        // not something ADR-057's "irreversible" requirement can skip.
        await vaultClient.V1.Secrets.Transit.UpdateEncryptionKeyConfigAsync(
            keyReference, new UpdateKeyRequestOptions { DeletionAllowed = true }, mountPoint);
        await vaultClient.V1.Secrets.Transit.DeleteEncryptionKeyAsync(keyReference, mountPoint);
    }

    private static string ToKeyName(string appId, string entityId) => $"dek-{appId}-{entityId}".Replace(':', '-');

    // Idempotent -- Vault returns an error if this mount path is already in
    // use, which just means an earlier call (or a previous test run against
    // a persistent dev server) already set it up.
    public static async Task EnsureTransitEngineMountedAsync(IVaultClient vaultClient, string mountPoint = "transit")
    {
        try
        {
            await vaultClient.V1.System.MountSecretBackendAsync(new SecretsEngine { Type = SecretsEngineType.Transit, Path = mountPoint });
        }
        catch (VaultApiException ex) when (ex.Message.Contains("path is already in use", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
