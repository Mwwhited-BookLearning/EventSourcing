using System;
using System.Data.SqlTypes;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.Server;

namespace EventStore.SqlClr.SqlServer
{
    // ADR-098 -- the SQL Server half of the in-database native predicate
    // evaluator seam. A pure, stateless scalar function: no database access
    // of its own (DataAccessKind.None, PERMISSION_SET = SAFE is enough),
    // deterministic, and scoped ONLY to the "Local" IErasureKeyStore/
    // ISearchIndexKeyStore backend -- the caller's own T-SQL query is
    // responsible for joining Events/EncryptedFieldIndexEntries against
    // LocalErasureKeyMaterials to supply the raw key bytes as a parameter;
    // this function never reaches out to a real KMS/Vault itself, which
    // would need external network access this ADR already names as a real,
    // separate constraint most SQLCLR deployments won't grant.
    //
    // DecryptAndCompareCore is the actual, testable logic -- deliberately
    // free of any Microsoft.SqlServer.Server dependency, so it can be
    // exercised directly by a plain unit test under the same net48 runtime
    // SQL Server's CLR host actually uses, not merely inferred correct by
    // analogy to EventStore.Erasure.EnvelopeAesGcm's separate net10.0
    // implementation. DecryptAndCompare below is the thin [SqlFunction]
    // wrapper SQL Server itself calls.
    public static class EncryptedPredicateFunctions
    {
        [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None, IsPrecise = true)]
        public static SqlBoolean DecryptAndCompare(
            SqlString ciphertextBase64, SqlBytes key, SqlString dataType, SqlString comparisonOperator, SqlString comparisonValue)
        {
            if (ciphertextBase64.IsNull || key.IsNull || dataType.IsNull || comparisonOperator.IsNull || comparisonValue.IsNull)
                return SqlBoolean.Null;

            try
            {
                return DecryptAndCompareCore(ciphertextBase64.Value, key.Value, dataType.Value, comparisonOperator.Value, comparisonValue.Value)
                    ? SqlBoolean.True
                    : SqlBoolean.False;
            }
            catch
            {
                // A row whose ciphertext can't be decrypted under the
                // supplied key (destroyed key, corrupt row, wrong key
                // passed by the calling query) can never satisfy a
                // comparison -- SqlBoolean.False, not an error that would
                // abort the whole query for one bad row.
                return SqlBoolean.False;
            }
        }

        // Mirrors EventStore.Erasure.EnvelopeAesGcm.Decrypt's exact wire
        // format (nonce || tag || ciphertext, 12-byte nonce, 16-byte tag --
        // AesGcm.NonceByteSizes.MaxSize/TagByteSizes.MaxSize on both
        // runtimes) and EventStore.Erasure.AppTierEncryptedPredicateEvaluator.
        // Satisfies' own comparison logic. Kept in exact lockstep with both
        // by design -- if either changes, this must change too.
        public static bool DecryptAndCompareCore(string ciphertextBase64, byte[] key, string dataType, string comparisonOperator, string comparisonValue)
        {
            var blob = Convert.FromBase64String(ciphertextBase64);
            const int nonceSize = 12;
            const int tagSize = 16;
            var nonce = new byte[nonceSize];
            Array.Copy(blob, 0, nonce, 0, nonceSize);
            var tag = new byte[tagSize];
            Array.Copy(blob, nonceSize, tag, 0, tagSize);
            var ciphertext = new byte[blob.Length - nonceSize - tagSize];
            Array.Copy(blob, nonceSize + tagSize, ciphertext, 0, ciphertext.Length);

            var plaintextBytes = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, tagSize))
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

            // PayloadEncryptor.EncryptLeafAsync encrypts the leaf's canonical
            // JSON text (realValue.ToJsonString()), e.g. `"42.5"` for a
            // string/number leaf or `"2026-03-15T00:00:00Z"` for a date --
            // always double-quoted JSON text, per that class's own comment.
            // Trimming the surrounding quotes recovers the same plain value
            // AppTierEncryptedPredicateEvaluator's own Satisfies compares.
            var plaintext = Encoding.UTF8.GetString(plaintextBytes).Trim('"');

            int comparison;
            switch (dataType)
            {
                case "Number":
                    comparison = double.Parse(plaintext, CultureInfo.InvariantCulture)
                        .CompareTo(double.Parse(comparisonValue, CultureInfo.InvariantCulture));
                    break;
                case "DateTimeOffset":
                    comparison = DateTimeOffset.Parse(plaintext, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                        .CompareTo(DateTimeOffset.Parse(comparisonValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
                    break;
                default:
                    comparison = string.CompareOrdinal(plaintext, comparisonValue);
                    break;
            }

            switch (comparisonOperator)
            {
                case "gt": return comparison > 0;
                case "gte": return comparison >= 0;
                case "lt": return comparison < 0;
                case "lte": return comparison <= 0;
                default: throw new ArgumentOutOfRangeException(nameof(comparisonOperator), comparisonOperator, "expected gt/gte/lt/lte");
            }
        }
    }
}
