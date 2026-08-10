using System.Text;
using System.Text.Json.Nodes;
using EventStore.Erasure;
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
public class PayloadMasker(
    IServiceProvider serviceProvider, IRedactorProvider redactorProvider, ILogger<PayloadMasker> logger, ErasureKeyService erasureKeyService)
    : IPayloadMasker
{
    public Task<JsonNode?> MaskAsync(JsonNode schema, JsonNode? payload, string? entityId, Func<string, bool> hasClaim, CancellationToken ct = default) =>
        MaskNodeAsync(schema as JsonObject, payload, payload, entityId, hasClaim, ct);

    private async Task<JsonNode?> MaskNodeAsync(
        JsonObject? schemaNode, JsonNode? payload, JsonNode? rootPayload, string? entityId, Func<string, bool> hasClaim, CancellationToken ct)
    {
        if (schemaNode is null || payload is null)
            return payload?.DeepClone();

        if (schemaNode.TryGetPropertyValue("x-masking", out var maskingNode) && maskingNode is JsonObject maskingConfig)
            return await MaskLeafAsync(payload, maskingConfig, rootPayload, entityId, hasClaim, ct);

        if (schemaNode["properties"] is JsonObject properties && payload is JsonObject payloadObject)
        {
            var result = new JsonObject();
            foreach (var (propertyName, propertyValue) in payloadObject)
                result[propertyName] = properties.TryGetPropertyValue(propertyName, out var propertySchema) && propertySchema is JsonObject propertySchemaObject
                    ? await MaskNodeAsync(propertySchemaObject, propertyValue, rootPayload, entityId, hasClaim, ct)
                    : propertyValue?.DeepClone();
            return result;
        }

        if (schemaNode["items"] is JsonObject itemsSchema && payload is JsonArray payloadArray)
        {
            var result = new JsonArray();
            foreach (var element in payloadArray)
                result.Add(await MaskNodeAsync(itemsSchema, element, rootPayload, entityId, hasClaim, ct));
            return result;
        }

        return payload.DeepClone();
    }

    private async Task<JsonNode> MaskLeafAsync(
        JsonNode realValue, JsonObject maskingConfig, JsonNode? rootPayload, string? entityId, Func<string, bool> hasClaim, CancellationToken ct)
    {
        var classification = maskingConfig["regulatoryClassification"]?.GetValue<string>();
        if (classification is not null)
        {
            var redactor = redactorProvider.GetRedactor(new DataClassification("MaskingLogRedaction", classification));
            logger.LogDebug("Evaluating masking for a {Classification}-classified field: {RedactedValue}",
                classification, redactor.Redact(ExtractRawText(realValue)));
        }

        var requiredClaim = maskingConfig["requiredClaim"]?.GetValue<string>();
        if (requiredClaim is not null && hasClaim(requiredClaim))
            return await RevealAsync(realValue, maskingConfig, rootPayload, entityId, classification, ct);

        var strategyName = maskingConfig["strategy"]!.GetValue<string>();
        var strategy = serviceProvider.GetRequiredKeyedService<IMaskingStrategy>(strategyName);
        return new JsonObject { ["masked"] = strategy.Mask(realValue, maskingConfig) };
    }

    // ADR-057 -- a claim holder sees {"erased": true} unconditionally once an
    // entity's DEK is destroyed, even though they hold every claim ("shown
    // even to a caller who holds every claim"). A claims-gated field that
    // was never classified (regulatoryClassification absent) has no DEK to
    // check at all and is revealed exactly as ADR-009 always did, before
    // ADR-057 existed.
    private async Task<JsonNode> RevealAsync(
        JsonNode realValue, JsonObject maskingConfig, JsonNode? rootPayload, string? entityId, string? classification, CancellationToken ct)
    {
        if (classification is null || entityId is null)
            return new JsonObject { ["value"] = realValue.DeepClone() };

        var scopedEntityId = ErasureScopeResolver.Resolve(rootPayload, maskingConfig, entityId);
        var resolved = await erasureKeyService.ResolveAsync(scopedEntityId, ct);
        if (resolved is null)
            return new JsonObject { ["value"] = realValue.DeepClone() };
        if (resolved.Value.Erased)
            return new JsonObject { ["erased"] = true };

        var ciphertextBytes = Convert.FromBase64String(realValue.GetValue<string>());
        var plaintextBytes = await resolved.Value.Backend.DecryptAsync(resolved.Value.KeyReference, ciphertextBytes, ct);
        if (plaintextBytes is null)
            return new JsonObject { ["erased"] = true };

        return new JsonObject { ["value"] = JsonNode.Parse(Encoding.UTF8.GetString(plaintextBytes)) };
    }

    internal static string ExtractRawText(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();
}
