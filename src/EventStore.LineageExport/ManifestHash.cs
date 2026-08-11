using System.Security.Cryptography;
using System.Text;

namespace EventStore.LineageExport;

// ADR-068: ManifestHash = SHA-256(ordered ChainHash values || ExportedByActorId
// || ExportedAt). Same pipe-delimited-concatenation-then-hash shape as
// EventStore.Domain.EventLog.EventChainHash.Compute -- one repo-wide
// convention for "hash a small, ordered set of fields," not reinvented here.
public static class ManifestHash
{
    public static string Compute(IEnumerable<string> orderedChainHashes, string exportedByActorId, DateTimeOffset exportedAt)
    {
        var input = string.Join("|", orderedChainHashes) + $"|{exportedByActorId}|{exportedAt:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
