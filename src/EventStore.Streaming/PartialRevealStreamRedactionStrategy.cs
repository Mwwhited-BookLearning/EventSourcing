using System.Text;
using System.Text.Json.Nodes;
using EventStore.Domain.Streaming;
using EventStore.Masking;

namespace EventStore.Streaming;

// ADR-052 -- reuses PartialRevealMaskingStrategy's reveal computation
// directly, rather than a second implementation of the same character-
// reveal logic. That strategy operates on a JsonNode/JsonObject config
// (EventStore.Masking's own value shape); this adapter is the bridge --
// decode the raw bytes as UTF-8 text, run the shared reveal computation,
// re-encode. Only meaningful for structured/string-shaped content
// (ADR-052's own text); a pure numeric waveform or video frame has no
// well-defined string form, so a caller configuring PartialReveal against
// one of those gets whatever this adapter's UTF-8 round trip produces --
// not validated against ContentKind here, the same way PartialReveal
// itself is never validated against a JSON property's own type.
public sealed class PartialRevealStreamRedactionStrategy : IStreamRedactionStrategy
{
    // Constructed directly, not injected -- PartialRevealMaskingStrategy is
    // stateless (no dependencies of its own), and EventStore.Masking only
    // ever registers it KEYED ("PartialReveal", for IPayloadMasker's own
    // resolution), which AddMasking()'s full composition (HMAC keys,
    // AddRedaction()) would otherwise have to exist just to satisfy this
    // adapter's own use of one stateless method.
    private readonly PartialRevealMaskingStrategy _inner = new();

    public byte[] Redact(byte[] realValue, RedactedRange range)
    {
        var text = Encoding.UTF8.GetString(realValue);
        var config = new JsonObject
        {
            ["showFirst"] = range.ShowFirst ?? 0,
            ["showLast"] = range.ShowLast ?? 0,
            ["maskChar"] = (range.MaskChar ?? 'X').ToString(),
            ["preserveSeparators"] = range.PreserveSeparators,
        };

        var revealed = _inner.Mask(JsonValue.Create(text), config);
        return Encoding.UTF8.GetBytes(revealed.GetValue<string>());
    }
}
