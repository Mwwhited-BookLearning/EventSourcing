using EventStore.Domain.Streaming;

namespace EventStore.Streaming;

// ADR-052 -- the sibling of EventStore.Masking's IMaskingStrategy, operating
// over raw sample/frame bytes rather than a JsonNode, since that's a
// genuinely different value shape. Parallel implementations of the same
// Strategy-pattern seam, not literally one shared interface.
public interface IStreamRedactionStrategy
{
    byte[] Redact(byte[] realValue, RedactedRange range);
}
