using EventStore.Domain.Streaming;

namespace EventStore.Streaming;

// ADR-052 -- a distinctive tone, not silence, for audio: SWGDE's own
// reasoning is that a silent redacted span can be confused with genuinely
// silent original content, the opposite of what redaction is supposed to
// signal. This framework has zero codec knowledge (ADR-030/031's own core-
// engine constraint), so a real decoded sine-wave tone at the channel's
// actual sample format is out of scope here -- this substitutes a fixed,
// non-zero repeating byte pattern, distinguishable from zero-fill/silence
// at the raw-byte level without decoding the frame, which is the concrete
// guarantee this ADR actually asks for.
public sealed class ToneStrategy : IStreamRedactionStrategy
{
    private const byte High = 0x7F;
    private const byte Low = 0x80;

    public byte[] Redact(byte[] realValue, RedactedRange range)
    {
        var result = new byte[realValue.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = i % 2 == 0 ? High : Low;
        return result;
    }
}
