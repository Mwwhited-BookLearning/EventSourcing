using System.Text.Json;
using System.Text.Json.Nodes;

namespace EventStore.SchemaRegistry;

// Hand-written, not JsonSchema.Net -- same reasoning as
// EventStore.SchemaRegistry.MaskingSchemaValidator's own structural check
// (see docs/changes/2026-08-03.md): every real schema in this design carries
// an undeclared "x-masking" vendor extension, which JsonSchema.Net's default
// dialect rejects outright. This validates a payload INSTANCE against a
// schema (required/type/properties/items) -- a different job from
// SchemaRegistryService's schema-DOCUMENT well-formedness check, but the
// same "tolerate any unrecognized keyword" posture, for the same reason.
// Public and living here (not EventStore.Inbox, its original home) so both
// EventStore.Inbox and EventStore.Router can use the identical check without
// either depending on the other (build-plan item 12, "Entity-Centric Core
// Rebuild": the Router, not the Inbox, is what actually calls this now).
public static class JsonSchemaInstanceValidator
{
    public static bool Validate(JsonNode? schema, JsonNode? payload, List<string> errors, string path = "$")
    {
        if (schema is not JsonObject schemaObject)
            return true; // no constraints (or a bare `true` schema) -- nothing to check

        var ok = true;

        if (schemaObject["type"] is { } typeNode)
        {
            var expectedTypes = typeNode is JsonArray typeArray
                ? typeArray.Select(t => t!.GetValue<string>())
                : [typeNode.GetValue<string>()];
            if (!expectedTypes.Any(t => MatchesType(t, payload)))
            {
                errors.Add($"{path}: expected type {string.Join(" or ", expectedTypes)}, got {DescribeActualType(payload)}");
                return false; // no point checking properties/items against the wrong shape
            }
        }

        if (schemaObject["required"] is JsonArray required)
        {
            if (payload is not JsonObject payloadObject)
                return false;
            foreach (var requiredName in required)
            {
                if (!payloadObject.ContainsKey(requiredName!.GetValue<string>()))
                {
                    errors.Add($"{path}: missing required property '{requiredName.GetValue<string>()}'");
                    ok = false;
                }
            }
        }

        if (schemaObject["properties"] is JsonObject properties && payload is JsonObject payloadObj)
        {
            foreach (var (propertyName, propertySchema) in properties)
            {
                if (payloadObj.TryGetPropertyValue(propertyName, out var propertyValue) && propertyValue is not null &&
                    !Validate(propertySchema, propertyValue, errors, $"{path}.{propertyName}"))
                    ok = false;
            }
        }

        if (schemaObject["items"] is { } itemsSchema && payload is JsonArray payloadArray)
        {
            for (var i = 0; i < payloadArray.Count; i++)
                if (!Validate(itemsSchema, payloadArray[i], errors, $"{path}[{i}]"))
                    ok = false;
        }

        return ok;
    }

    private static bool MatchesType(string type, JsonNode? value)
    {
        if (value is null)
            return type == "null";

        var kind = value.GetValueKind();
        return type switch
        {
            "object" => kind == JsonValueKind.Object,
            "array" => kind == JsonValueKind.Array,
            "string" => kind == JsonValueKind.String,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && IsIntegerValued(value),
            "null" => kind == JsonValueKind.Null,
            _ => true, // unrecognized type keyword value -- tolerate, don't fail closed on our own uncertainty
        };
    }

    private static bool IsIntegerValued(JsonNode value)
    {
        var d = value.GetValue<double>();
        return d == Math.Floor(d) && !double.IsInfinity(d);
    }

    private static string DescribeActualType(JsonNode? value) =>
        value is null ? "null" : value.GetValueKind().ToString();
}
