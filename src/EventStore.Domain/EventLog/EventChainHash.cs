using System.Security.Cryptography;
using System.Text;

namespace EventStore.Domain.EventLog;

// ADR-019: ChainHash[n] = SHA-256(ChainHash[n-1] || PayloadHash[n] || SequenceNumber[n]).
// Shared by PublishService (computes it at insert time) and any chain
// verification service (recomputes it to detect tampering) -- one formula,
// not reimplemented at each call site.
public static class EventChainHash
{
    // The fixed seed ChainHash[0] that SequenceNumber = 1 chains off of, the
    // store's first-ever event. 64 chars, matching a real SHA-256 hex digest's
    // length, so genesis and every subsequent link share one shape.
    public static readonly string Genesis = new('0', 64);

    public static string Compute(string priorChainHash, string payloadHash, long sequenceNumber) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{priorChainHash}|{payloadHash}|{sequenceNumber}"))).ToLowerInvariant();
}
