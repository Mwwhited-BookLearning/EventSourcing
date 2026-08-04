using EventStore.Domain.Streaming;

namespace EventStore.Streaming;

// ADR-052's ContentKind-appropriate default for RawScalar/RawBinary -- a
// run of 0x00 matching the real value's own length/cadence. Requires zero
// domain-specific knowledge of the channel's real content.
public sealed class ZeroFillStrategy : IStreamRedactionStrategy
{
    public byte[] Redact(byte[] realValue, RedactedRange range) => new byte[realValue.Length];
}
