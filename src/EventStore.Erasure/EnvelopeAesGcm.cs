using System.Security.Cryptography;

namespace EventStore.Erasure;

// Shared AES-256-GCM envelope logic, extracted from LocalErasureKeyStore's
// own original private methods so every IErasureKeyStore backend that
// needs to encrypt an arbitrary-length field value locally (any cloud KMS
// backend, which wraps/unwraps a local Data-Encryption Key rather than
// encrypting field values directly -- see AzureKeyVaultErasureKeyStore's
// own header comment for why) shares ONE implementation instead of
// re-deriving it per backend and risking drift between copies.
//
// Convergent (deterministic-nonce) by design, not an oversight: ADR-011's
// publish idempotency compares PayloadHash, computed over the STORED
// (post-encryption) payload -- a random nonce would make retrying an
// identical logical publish (same eventId, same real field values) hash
// differently every time and falsely report a 409 Conflict instead of an
// idempotent replay. The accepted, well-known cost of convergent
// encryption -- two ciphertexts reveal whether they encrypt the same
// plaintext -- is not a concern for THIS threat model (crypto-shredding's
// goal is making a value permanently unrecoverable on erasure, not hiding
// equality between still-live values).
public static class EnvelopeAesGcm
{
    public static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var nonce = DeriveNonce(key, plaintext);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    public static byte[] Decrypt(byte[] key, byte[] blob)
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

    private static byte[] DeriveNonce(byte[] key, byte[] plaintext) =>
        HMACSHA256.HashData(key, plaintext)[..AesGcm.NonceByteSizes.MaxSize];
}
