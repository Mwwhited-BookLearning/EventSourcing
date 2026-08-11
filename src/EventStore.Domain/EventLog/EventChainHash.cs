using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    // ADR-066's own claim ("non-repudiation reuses the existing hash chain,
    // no new primitive... exactly as tamper-evident as everything else in
    // the log") doesn't hold against PayloadHash alone -- Signature is
    // envelope metadata, never part of {EventType, Payload, parentEventIds}.
    // Folded into ChainHash specifically, not PayloadHash: PayloadHash is
    // also ADR-011's idempotency-comparison basis, and Signature.SignedAt is
    // wall-clock-real at each publish attempt, not deterministic/convergent
    // the way ADR-057's own classified-field ciphertext had to be made --
    // including it there would make every legitimate retry of a signed
    // publish look like different content. ChainHash is never compared for
    // idempotency (only PayloadHash is, in ReplayOrConflict), so extending
    // IT costs nothing there while still making a tamper to SignerId/
    // SignedAt/Meaning/Acr diverge the chain at exactly that
    // SequenceNumber. Omitted entirely (not hashed as a literal "null") for
    // an unsigned event, so every event type that never uses
    // RequiredSignature computes byte-identical ChainHash values to before
    // this parameter existed.
    public static string Compute(string priorChainHash, string payloadHash, long sequenceNumber, Signature? signature = null)
    {
        var input = $"{priorChainHash}|{payloadHash}|{sequenceNumber}";
        if (signature is not null)
            input += $"|{JsonSerializer.Serialize(signature)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
