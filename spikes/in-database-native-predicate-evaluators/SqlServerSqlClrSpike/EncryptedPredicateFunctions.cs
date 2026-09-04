using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
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

            // PureNet48AesGcm, not System.Security.Cryptography.AesGcm --
            // direct request, 2026-09-04: build the SQLCLR side with pure
            // net48, no NuGet/.NET-Standard extension packages at all. The
            // BCL's own AesGcm needs Microsoft.Bcl.Cryptography under net48
            // (AesGcm itself is .NET Standard 2.1+ only), and that
            // package's own transitive dependency chain is what failed SQL
            // Server's CLR verifier under SAFE -- see this project's own
            // PureNet48AesGcm.cs header for the full investigation and the
            // from-scratch GCM construction this now uses instead.
            var plaintextBytes = PureNet48AesGcm.Decrypt(key, nonce, ciphertext, tag);

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

        // Real, previously-unknown finding, 2026-09-04, direct request
        // ("what about a version that would use a sqlclr table function or
        // stored procedure so they are processed in blocks? I've done
        // vector processing in the database and has better performance"):
        // DecryptAndCompare above is a SCALAR function -- SQL Server calls
        // it once PER ROW inside the query engine's own row-processing
        // loop, each call crossing from SQLOS's own cooperative scheduler
        // into the CLR host, a real, measured cost (see docs/08-build-
        // plan.md item 56's own benchmark section: SQLCLR was SLOWER than
        // the plain app-tier default at 50,000 rows, for exactly this
        // reason). A genuine batch/table-valued function pays that
        // crossing cost ONCE for the whole candidate set, not once per
        // row -- true vector processing, not 50,000 individual calls.
        //
        // Table-valued PARAMETERS cannot be passed as CLR routine INPUT
        // at all (a real, confirmed SQL Server limitation, verified via
        // Microsoft's own Q&A before building this -- CREATE FUNCTION/
        // PROCEDURE with a TVP-typed CLR parameter fails with a type
        // mismatch error). So this function does its OWN candidate-set
        // query via the "context connection" (the same in-process
        // connection the calling batch is already running under -- no
        // network hop, still SAFE-permission-set-compatible) rather than
        // receiving pre-fetched candidate rows -- the identical, cheap,
        // already-indexed equality lookup ADR-096's own bucket-narrowing
        // step already performs, just run once, inside this one call,
        // instead of by the caller beforehand.
        //
        // DELIBERATELY SCOPED TO THIS SESSION'S OWN BENCHMARK TABLE
        // (dbo.bench_rows), not the real EncryptedFieldIndexEntries
        // schema -- proving the batching mechanism's real performance
        // effect first, honestly, rather than half-adapting the real
        // schema without measuring whether it's worth doing at all.
        // Generalizing to the real (AppId, EventTypeName, FieldJsonPath,
        // IndexKind) filter this project's own production schema uses is
        // named as real, separate follow-up work, not done by this pass.
        [SqlFunction(FillRowMethodName = "FillSequenceNumberRow", TableDefinition = "SequenceNumber BIGINT", DataAccess = DataAccessKind.Read)]
        public static IEnumerable DecryptAndCompareBatchBench(SqlBytes key, SqlString dataType, SqlString comparisonOperator, SqlString comparisonValue)
        {
            var matches = new List<long>();
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT id, token FROM dbo.bench_rows", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetInt32(0);
                        var token = reader.GetString(1);
                        bool isMatch;
                        try
                        {
                            isMatch = DecryptAndCompareCore(token, key.Value, dataType.Value, comparisonOperator.Value, comparisonValue.Value);
                        }
                        catch
                        {
                            isMatch = false; // same "unreadable row never satisfies" posture as the scalar function above
                        }
                        if (isMatch)
                            matches.Add(id);
                    }
                }
            }
            return matches;
        }

        private static void FillSequenceNumberRow(object rowObject, out long sequenceNumber)
        {
            sequenceNumber = (long)rowObject;
        }
    }
}
