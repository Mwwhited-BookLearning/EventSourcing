using EventStore.Domain.Streaming;

namespace EventStore.Streaming;

// ADR-052 -- a blank/black frame for video. Unlike audio silence (ToneStrategy's
// own reasoning), an all-zero byte pattern genuinely IS a valid raw
// representation of "black" in most pixel formats, and this ADR raises no
// "confusable with real content" concern for video the way it does for
// audio -- so, honestly scoped to what a codec-agnostic core engine (no
// knowledge of the channel's actual codec/container, ADR-030/031) can
// actually guarantee, this is the same zero-fill substitution as
// ZeroFillStrategy, kept as its own class for the same reason ToneStrategy
// is its own class: a future real per-codec implementation would only need
// to change this one, without touching the others.
public sealed class BlankFrameStrategy : IStreamRedactionStrategy
{
    private const byte Marker = 0x00;

    public byte[] Redact(byte[] realValue, RedactedRange range)
    {
        var result = new byte[realValue.Length];
        Array.Fill(result, Marker);
        return result;
    }
}
