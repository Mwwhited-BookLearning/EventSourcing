using System.Text.Json.Nodes;

namespace EventStore.Masking;

// x-masking: { "strategy": "FixedValue", "maskedValue": "***" } -- maskedValue
// defaults to "***" if omitted (ADR-009).
public sealed class FixedValueMaskingStrategy : IMaskingStrategy
{
    public JsonNode Mask(JsonNode realValue, JsonObject maskingConfig) =>
        JsonValue.Create(maskingConfig["maskedValue"]?.GetValue<string>() ?? "***");
}
