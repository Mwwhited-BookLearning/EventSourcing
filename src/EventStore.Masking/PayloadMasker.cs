using System.Text.Json.Nodes;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Masking;

// docs/06-solution-structure.md's IPayloadMasker sketch used JsonSchema (a
// JsonSchema.Net type); this codebase reverted that library entirely while
// building "Schema Registry" (see docs/changes/2026-08-03.md -- undeclared
// vendor keywords like x-masking aren't tolerated by its default parse) in
// favor of a plain System.Text.Json.Nodes tree-walk, the same style
// MaskingSchemaValidator already established. This class follows that same,
// already-verified approach rather than the sketch's now-stale type.
//
// A "MaskingLogRedaction" taxonomy (distinct from HashMaskingStrategy's
// "MaskingHmacKey") drives ADR-050's separate log-redaction sink: whenever a
// masked leaf carries x-masking.regulatoryClassification, the real value is
// logged (at Debug level, for diagnostics) only through IRedactorProvider's
// matching redactor -- reusing the same classification metadata ADR-009
// already declares, for a sink the original query/stream-response-only
// masking never covered.
public class PayloadMasker(IServiceProvider serviceProvider, IRedactorProvider redactorProvider, ILogger<PayloadMasker> logger) : IPayloadMasker
{
    public JsonNode? Mask(JsonNode schema, JsonNode? payload, Func<string, bool> hasClaim) =>
        MaskNode(schema as JsonObject, payload, hasClaim);

    private JsonNode? MaskNode(JsonObject? schemaNode, JsonNode? payload, Func<string, bool> hasClaim)
    {
        if (schemaNode is null || payload is null)
            return payload?.DeepClone();

        if (schemaNode.TryGetPropertyValue("x-masking", out var maskingNode) && maskingNode is JsonObject maskingConfig)
            return MaskLeaf(payload, maskingConfig, hasClaim);

        if (schemaNode["properties"] is JsonObject properties && payload is JsonObject payloadObject)
        {
            var result = new JsonObject();
            foreach (var (propertyName, propertyValue) in payloadObject)
                result[propertyName] = properties.TryGetPropertyValue(propertyName, out var propertySchema) && propertySchema is JsonObject propertySchemaObject
                    ? MaskNode(propertySchemaObject, propertyValue, hasClaim)
                    : propertyValue?.DeepClone();
            return result;
        }

        if (schemaNode["items"] is JsonObject itemsSchema && payload is JsonArray payloadArray)
        {
            var result = new JsonArray();
            foreach (var element in payloadArray)
                result.Add(MaskNode(itemsSchema, element, hasClaim));
            return result;
        }

        return payload.DeepClone();
    }

    private JsonNode MaskLeaf(JsonNode realValue, JsonObject maskingConfig, Func<string, bool> hasClaim)
    {
        if (maskingConfig["regulatoryClassification"]?.GetValue<string>() is { } classification)
        {
            var redactor = redactorProvider.GetRedactor(new DataClassification("MaskingLogRedaction", classification));
            logger.LogDebug("Evaluating masking for a {Classification}-classified field: {RedactedValue}",
                classification, redactor.Redact(ExtractRawText(realValue)));
        }

        var requiredClaim = maskingConfig["requiredClaim"]?.GetValue<string>();
        if (requiredClaim is not null && hasClaim(requiredClaim))
            return new JsonObject { ["value"] = realValue.DeepClone() };

        var strategyName = maskingConfig["strategy"]!.GetValue<string>();
        var strategy = serviceProvider.GetRequiredKeyedService<IMaskingStrategy>(strategyName);
        return new JsonObject { ["masked"] = strategy.Mask(realValue, maskingConfig) };
    }

    internal static string ExtractRawText(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();
}
