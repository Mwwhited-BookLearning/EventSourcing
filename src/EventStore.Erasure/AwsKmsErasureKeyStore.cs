using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;

namespace EventStore.Erasure;

// The AWS cloud tier (ADR-057) -- real AWSSDK.KeyManagementService,
// verified against the actual installed package before writing this.
//
// One AWS KMS customer-managed key (CMK) PER ENTITY, not the single
// per-AppId alias `docs/libraries/dotnet/aws-kms.md`'s own general-usage
// sketch shows -- a deliberate correction, not an oversight. A shared
// CMK across many entities (AWS's own idiomatic, quota-friendly pattern
// for "one key, many data keys") cannot satisfy ADR-057's per-entity
// crypto-shredding: destroying that ONE shared CMK to erase a single
// entity would also erase every OTHER entity's data still wrapped under
// it, and there is no way to selectively revoke a shared CMK's ability
// to decrypt just one entity's own already-issued ciphertext. A CMK per
// entity has a real, honestly-stated cost this backend accepts that
// Vault/Azure/GCP's own named-key-per-entity model doesn't face the same
// way (AWS account-level CMK quotas) -- but it's the only way this
// backend's own DestroyKeyAsync means the same thing the other three
// already do.
//
// Envelope encryption, same shape as AzureKeyVaultErasureKeyStore (see
// that class's own header comment for why: KMS's native Encrypt has a
// 4KB plaintext limit and, more importantly, is not what a per-entity
// CMK is FOR -- GenerateDataKey/Decrypt is the documented pattern for
// wrapping a LOCAL Data-Encryption Key once, then encrypting arbitrary-
// length values with it directly via EnvelopeAesGcm's own deterministic-
// nonce AES-256-GCM, preserving ADR-011's publish-idempotency guarantee
// identically across every backend).
public class AwsKmsErasureKeyStore(IAmazonKeyManagementService kms) : IErasureKeyStore
{
    public async Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default)
    {
        var createResponse = await kms.CreateKeyAsync(new CreateKeyRequest
        {
            Description = $"EventStore erasure DEK-wrapping key for {appId}/{entityId}",
        }, ct);
        var keyId = createResponse.KeyMetadata.KeyId;

        var dataKeyResponse = await kms.GenerateDataKeyAsync(new GenerateDataKeyRequest
        {
            KeyId = keyId,
            KeySpec = DataKeySpec.AES_256,
        }, ct);
        return ToReference(keyId, dataKeyResponse.CiphertextBlob.ToArray());
    }

    public async Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct)
            ?? throw new InvalidOperationException($"AWS KMS erasure key '{keyReference}' does not exist or has been destroyed");
        return EnvelopeAesGcm.Encrypt(dek, plaintext);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct);
        return dek is null ? null : EnvelopeAesGcm.Decrypt(dek, ciphertext);
    }

    public async Task DestroyKeyAsync(string keyReference, CancellationToken ct = default)
    {
        var (keyId, _) = FromReference(keyReference);
        // AWS KMS refuses immediate CMK deletion by design -- the shortest
        // allowed pending window is 7 days (Amazon's own documented
        // minimum for ScheduleKeyDeletion), a real, KMS-imposed delay
        // unlike Vault/Azure/Local's own immediate destruction. Still
        // irreversible once the window elapses, matching this ADR's own
        // requirement and the library doc's own sketch.
        await kms.ScheduleKeyDeletionAsync(new ScheduleKeyDeletionRequest
        {
            KeyId = keyId,
            PendingWindowInDays = 7,
        }, ct);
    }

    private async Task<byte[]?> UnwrapDekAsync(string keyReference, CancellationToken ct)
    {
        var (keyId, wrappedDek) = FromReference(keyReference);
        try
        {
            using var ciphertextStream = new MemoryStream(wrappedDek);
            var response = await kms.DecryptAsync(new DecryptRequest
            {
                KeyId = keyId,
                CiphertextBlob = ciphertextStream,
            }, ct);
            return response.Plaintext.ToArray();
        }
        catch (KMSInvalidStateException)
        {
            // The CMK is pending deletion or already deleted -- ADR-057's
            // own contract for this method is "null means erased," never
            // a transient error to bubble up, the same posture
            // HashiCorpVaultErasureKeyStore's/AzureKeyVaultErasureKeyStore's
            // own catch already establishes for their own backends.
            return null;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private static string ToReference(string keyId, byte[] wrappedDek) => $"{keyId}:{Convert.ToBase64String(wrappedDek)}";

    private static (string KeyId, byte[] WrappedDek) FromReference(string keyReference)
    {
        var separatorIndex = keyReference.IndexOf(':');
        return (keyReference[..separatorIndex], Convert.FromBase64String(keyReference[(separatorIndex + 1)..]));
    }
}
