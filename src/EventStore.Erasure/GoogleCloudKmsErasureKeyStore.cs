using System.Security.Cryptography;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Kms.V1;
using Grpc.Core;

namespace EventStore.Erasure;

// The Google Cloud tier (ADR-057) -- real Google.Cloud.Kms.V1, verified
// against the actual installed package before writing this. One
// symmetric CryptoKey PER ENTITY under a single, pre-existing KeyRing
// (created once per deployment, outside this class -- GCP KMS KeyRings
// cannot be deleted, so creating one per entity would leak forever;
// CryptoKeys inside a KeyRing CAN be destroyed, which is what ADR-057's
// erasure actually needs).
//
// Envelope encryption, same shape/reasoning as AzureKeyVaultErasureKeyStore
// and AwsKmsErasureKeyStore: GCP KMS's own symmetric Encrypt/Decrypt is
// NOT deterministic (AES-256-GCM with a fresh random IV per call), which
// would break ADR-011's publish-idempotency comparison the same way a
// naive random-nonce local scheme would. CreateKeyAsync wraps a LOCAL
// AES-256 Data-Encryption Key once via GCP KMS's own Encrypt (a 32-byte
// payload, trivially under any size limit); EnvelopeAesGcm's own
// deterministic-nonce AES-256-GCM does the actual, repeatable field-value
// encryption locally.
public class GoogleCloudKmsErasureKeyStore(KeyManagementServiceClient kmsClient, string projectId, string locationId, string keyRingId) : IErasureKeyStore
{
    public async Task<string> CreateKeyAsync(string appId, string entityId, CancellationToken ct = default)
    {
        var cryptoKeyId = ToCryptoKeyId(appId, entityId);
        var keyRingName = new KeyRingName(projectId, locationId, keyRingId);
        var cryptoKey = await kmsClient.CreateCryptoKeyAsync(keyRingName, cryptoKeyId, new CryptoKey
        {
            Purpose = CryptoKey.Types.CryptoKeyPurpose.EncryptDecrypt,
        }, ct);

        var dek = RandomNumberGenerator.GetBytes(32); // AES-256
        var encryptResponse = await kmsClient.EncryptAsync(cryptoKey.CryptoKeyName, Google.Protobuf.ByteString.CopyFrom(dek), ct);
        return ToReference(cryptoKeyId, encryptResponse.Ciphertext.ToByteArray());
    }

    public async Task<byte[]> EncryptAsync(string keyReference, byte[] plaintext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct)
            ?? throw new InvalidOperationException($"Google Cloud KMS erasure key '{keyReference}' does not exist or has been destroyed");
        return EnvelopeAesGcm.Encrypt(dek, plaintext);
    }

    public async Task<byte[]?> DecryptAsync(string keyReference, byte[] ciphertext, CancellationToken ct = default)
    {
        var dek = await UnwrapDekAsync(keyReference, ct);
        return dek is null ? null : EnvelopeAesGcm.Decrypt(dek, ciphertext);
    }

    public async Task DestroyKeyAsync(string keyReference, CancellationToken ct = default)
    {
        var (cryptoKeyId, _) = FromReference(keyReference);
        var versionName = new CryptoKeyVersionName(projectId, locationId, keyRingId, cryptoKeyId, "1");
        // GCP KMS refuses immediate destruction by design -- the shortest
        // allowed window is 24 hours (Cloud KMS's own documented minimum
        // for DestroyCryptoKeyVersion), a real, KMS-imposed delay unlike
        // Vault/Azure/Local's own immediate destruction, the same honest
        // constraint AwsKmsErasureKeyStore's own 7-day minimum documents
        // for AWS. Still irreversible once the window elapses.
        await kmsClient.DestroyCryptoKeyVersionAsync(versionName, ct);
    }

    private async Task<byte[]?> UnwrapDekAsync(string keyReference, CancellationToken ct)
    {
        var (cryptoKeyId, wrappedDek) = FromReference(keyReference);
        var cryptoKeyName = new CryptoKeyName(projectId, locationId, keyRingId, cryptoKeyId);
        try
        {
            var response = await kmsClient.DecryptAsync(cryptoKeyName, Google.Protobuf.ByteString.CopyFrom(wrappedDek), ct);
            return response.Plaintext.ToByteArray();
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.FailedPrecondition)
        {
            // The CryptoKeyVersion is destroyed/scheduled for destruction,
            // or never existed -- ADR-057's own contract for this method is
            // "null means erased," never a transient error to bubble up,
            // the same posture the Azure/AWS/Vault backends' own catches
            // already establish for their respective SDKs.
            return null;
        }
    }

    private static string ToCryptoKeyId(string appId, string entityId) => $"dek-{appId}-{entityId}".Replace(':', '-');

    private static string ToReference(string cryptoKeyId, byte[] wrappedDek) => $"{cryptoKeyId}:{Convert.ToBase64String(wrappedDek)}";

    private static (string CryptoKeyId, byte[] WrappedDek) FromReference(string keyReference)
    {
        var separatorIndex = keyReference.IndexOf(':');
        return (keyReference[..separatorIndex], Convert.FromBase64String(keyReference[(separatorIndex + 1)..]));
    }
}
