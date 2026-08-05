using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace EventStore.Domain.AccessLog;

// ADR-045's own "<entry fields>" half of ChainHash[n] = SHA-256(ChainHash[n-1]
// || <entry fields> || SequenceNumber[n]) -- the AccessLog analogue of
// EventPayloadHash, fed into EventChainHash.Compute (reused unchanged; its
// second parameter is generically "the thing being chained," not specific to
// StoredEvent).
public static class AccessLogEntryHash
{
    public static string Compute(AccessLogEntry entry)
    {
        var canonical = new JsonObject
        {
            ["readerActorId"] = entry.ReaderActorId,
            ["readerTrustBasis"] = entry.ReaderTrustBasis,
            ["grantRef"] = entry.GrantRef?.ToString(),
            ["viewAccessed"] = entry.ViewAccessed,
            ["resourceRef"] = entry.ResourceRef,
            ["action"] = entry.Action,
            ["accessedAt"] = entry.AccessedAt.ToUnixTimeMilliseconds(),
        };
        var bytes = Encoding.UTF8.GetBytes(canonical.ToJsonString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
