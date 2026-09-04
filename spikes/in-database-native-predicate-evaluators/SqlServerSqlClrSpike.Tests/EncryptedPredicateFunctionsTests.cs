using System;
using EventStore.SqlClr.SqlServer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.SqlClr.SqlServer.Tests
{
    // ADR-098 -- cross-runtime compatibility check: KEY/CIPHERTEXT/CIPHERTEXT2
    // below are real, golden values generated this session by actually
    // calling EventStore.Erasure.EnvelopeAesGcm.Encrypt under net10.0 (the
    // real production encryption path, ADR-057) -- not values invented to
    // match this project's own decrypt logic. Passing them here proves
    // DecryptAndCompareCore (net48, the runtime SQL Server's CLR host
    // actually loads) can correctly decrypt ciphertext the real system
    // produces, not merely that this file's own encrypt-then-decrypt
    // round-trips against itself.
    [TestClass]
    public class EncryptedPredicateFunctionsTests
    {
        private const string Key = "4fs+TJWaTTE9sx19HWvqYYXWPY072Nm/32mJxqFCYD0=";
        private const string NumberCiphertext = "LVU6ANGl+u5gD7TQb2aYy0bvGOFgUmVEAECvpP7ShJautA=="; // decrypts to "42.5"
        private const string DateCiphertext = "ljAzgGlMwjhP43L9INTKooy/xc4xmqiSUWBnDso6XRQTFsiKEWB6mmYjg1ufdIQ4yWo="; // decrypts to "2026-03-15T00:00:00Z"

        private static byte[] KeyBytes => Convert.FromBase64String(Key);

        [TestMethod]
        public void DecryptsRealEnvelopeAesGcmCiphertextAndComparesNumbersCorrectly()
        {
            Assert.IsTrue(EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, KeyBytes, "Number", "gt", "40"));
            Assert.IsFalse(EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, KeyBytes, "Number", "gt", "50"));
            Assert.IsTrue(EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, KeyBytes, "Number", "gte", "42.5"));
            Assert.IsTrue(EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, KeyBytes, "Number", "lt", "100"));
            Assert.IsFalse(EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, KeyBytes, "Number", "lte", "10"));
        }

        [TestMethod]
        public void DecryptsRealEnvelopeAesGcmCiphertextAndComparesDatesCorrectly()
        {
            Assert.IsTrue(EncryptedPredicateFunctions.DecryptAndCompareCore(DateCiphertext, KeyBytes, "DateTimeOffset", "gt", "2026-01-01T00:00:00Z"));
            Assert.IsFalse(EncryptedPredicateFunctions.DecryptAndCompareCore(DateCiphertext, KeyBytes, "DateTimeOffset", "gt", "2026-12-01T00:00:00Z"));
            Assert.IsTrue(EncryptedPredicateFunctions.DecryptAndCompareCore(DateCiphertext, KeyBytes, "DateTimeOffset", "lte", "2026-03-15T00:00:00Z"));
        }

        [TestMethod]
        public void AWrongKeyFailsToDecryptRatherThanSilentlyProducingGarbage()
        {
            // System.Security.Cryptography.CryptographicException, not
            // AuthenticationTagMismatchException (a .NET Standard 2.1+/
            // AesGcm-specific type) -- PureNet48AesGcm (2026-09-04, direct
            // request: pure net48, no NET-Standard extensions) throws the
            // classic, always-available net48 BCL exception type instead.
            var wrongKey = new byte[32]; // all zeros -- deliberately not Key
            Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>(() =>
                EncryptedPredicateFunctions.DecryptAndCompareCore(NumberCiphertext, wrongKey, "Number", "gt", "0"));
        }

        [TestMethod]
        public void TheSqlFunctionWrapperReturnsNullWhenAnyArgumentIsSqlNull()
        {
            var result = EncryptedPredicateFunctions.DecryptAndCompare(
                System.Data.SqlTypes.SqlString.Null, new System.Data.SqlTypes.SqlBytes(KeyBytes),
                "Number", "gt", "0");
            Assert.IsTrue(result.IsNull);
        }

        [TestMethod]
        public void TheSqlFunctionWrapperReturnsFalseNotAnErrorWhenDecryptionFails()
        {
            var result = EncryptedPredicateFunctions.DecryptAndCompare(
                NumberCiphertext, new System.Data.SqlTypes.SqlBytes(new byte[32]), "Number", "gt", "0");
            Assert.IsFalse(result.IsNull);
            Assert.IsFalse(result.Value);
        }
    }
}
