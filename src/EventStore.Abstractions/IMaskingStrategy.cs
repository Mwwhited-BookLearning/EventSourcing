using System.Text.Json.Nodes;

namespace EventStore.Masking;

// Strategy-pattern seam (ADR-009) -- IPayloadMasker never branches on the
// strategy name, only resolves the matching keyed IMaskingStrategy per
// masked leaf. Pure per call: no I/O, no ambient state beyond whatever a
// strategy's own constructor captured (HashMaskingStrategy's IRedactorProvider).
public interface IMaskingStrategy
{
    JsonNode Mask(JsonNode realValue, JsonObject maskingConfig);
}
