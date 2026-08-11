using System.Text.Json.Nodes;

namespace EventStore.SchemaRegistry;

// "Compatibility & Deployment Discipline" (ADR-038's enum-fallback
// contract): x-enum-fallback is a per-field, registration-time opt-in --
// only meaningful alongside JSON Schema's own standard "enum" keyword (the
// known-values list an old client checks a since-arrived value against),
// and only on a string-typed property (this design's enum-like fields are
// always declared as string + enum, never a dedicated schema "enum type"
// of their own). Structural-only, same posture as MaskingSchemaValidator's
// sibling check: pure data validation on the registration payload, no
// claims involved. Mutually exclusive with x-masking on the same property --
// EventTypeSchemaReader/FollowSubscriptionTypeModule's dynamic field
// builders only ever handle one or the other, never both at once.
internal static class EnumFallbackSchemaValidator
{
    public static void Validate(JsonObject? node, List<string> errors)
    {
        if (node is null) return;

        var type = node["type"]?.GetValue<string>();

        if (node.TryGetPropertyValue("x-enum-fallback", out var fallbackNode))
        {
            if (fallbackNode is not JsonValue value || !value.TryGetValue<bool>(out _))
                errors.Add("x-enum-fallback must be a boolean");
            else if (type != "string")
                errors.Add("x-enum-fallback can only be placed on a string-typed property");
            else if (node["enum"] is not JsonArray enumValues || enumValues.Count == 0)
                errors.Add("x-enum-fallback requires a non-empty \"enum\" array on the same property");
            else if (node.ContainsKey("x-masking"))
                errors.Add("x-enum-fallback and x-masking cannot both be set on the same property");
        }

        if (node["properties"] is JsonObject properties)
            foreach (var (_, propertySchema) in properties)
                Validate(propertySchema as JsonObject, errors);

        if (node["items"] is JsonObject items)
            Validate(items, errors);
    }
}
