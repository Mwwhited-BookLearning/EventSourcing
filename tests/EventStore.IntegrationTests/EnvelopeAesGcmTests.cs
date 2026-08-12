using System.Text;
using EventStore.Erasure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// EnvelopeAesGcm is the shared primitive AzureKeyVaultErasureKeyStore/
// AwsKmsErasureKeyStore/GoogleCloudKmsErasureKeyStore all depend on for
// their own field-value encryption (each wraps a local Data-Encryption
// Key via its own cloud KMS, then uses THIS for the actual, arbitrary-
// length ciphertext -- see AzureKeyVaultErasureKeyStore's own header
// comment for why none of the three use their KMS's native encrypt
// directly). No live cloud account is needed to verify this class
// itself -- it's a pure function of (key, plaintext) with no external
// dependency at all, unlike the 3 KMS backends' own key-wrap/unwrap
// calls, which genuinely do need a real account this environment has no
// credentials for.
[TestClass]
public class EnvelopeAesGcmTests
{
    [TestMethod]
    public void EncryptThenDecryptRoundTripsToTheOriginalPlaintext()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var plaintext = Encoding.UTF8.GetBytes("123-45-6789");

        var ciphertext = EnvelopeAesGcm.Encrypt(key, plaintext);
        var decrypted = EnvelopeAesGcm.Decrypt(key, ciphertext);

        CollectionAssert.AreEqual(plaintext, decrypted);
    }

    // ADR-011's publish idempotency compares PayloadHash computed over the
    // STORED (post-encryption) payload -- every IErasureKeyStore backend's
    // own field-value encryption MUST be deterministic (convergent), or a
    // legitimate retry of an identical publish would hash differently and
    // be wrongly reported as a 409 Conflict instead of an idempotent
    // replay. This is the one property that actually matters here, not
    // just "it round-trips."
    [TestMethod]
    public void EncryptingTheSamePlaintextUnderTheSameKeyTwiceProducesIdenticalCiphertext()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var plaintext = Encoding.UTF8.GetBytes("the same real field value, twice");

        var first = EnvelopeAesGcm.Encrypt(key, plaintext);
        var second = EnvelopeAesGcm.Encrypt(key, plaintext);

        CollectionAssert.AreEqual(first, second, "encryption must be convergent -- an identical retry must hash identically, not look like a genuinely different write");
    }

    [TestMethod]
    public void EncryptingTheSamePlaintextUnderDifferentKeysProducesDifferentCiphertext()
    {
        var keyA = new byte[32];
        var keyB = new byte[32];
        Random.Shared.NextBytes(keyA);
        Random.Shared.NextBytes(keyB);
        var plaintext = Encoding.UTF8.GetBytes("same value, different entity's key");

        var ciphertextA = EnvelopeAesGcm.Encrypt(keyA, plaintext);
        var ciphertextB = EnvelopeAesGcm.Encrypt(keyB, plaintext);

        CollectionAssert.AreNotEqual(ciphertextA, ciphertextB);
    }

    [TestMethod]
    public void DifferentPlaintextUnderTheSameKeyProducesDifferentCiphertext()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var ciphertextA = EnvelopeAesGcm.Encrypt(key, Encoding.UTF8.GetBytes("value one"));
        var ciphertextB = EnvelopeAesGcm.Encrypt(key, Encoding.UTF8.GetBytes("value two"));

        CollectionAssert.AreNotEqual(ciphertextA, ciphertextB);
    }
}
