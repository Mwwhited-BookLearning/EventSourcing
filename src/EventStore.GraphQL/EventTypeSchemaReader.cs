using System.Text.Json.Nodes;

namespace EventStore.GraphQL;

public enum GraphQlScalarKind { String, Float, Boolean, DateTimeOffset }

// EnumFallback/KnownValues -- "Compatibility & Deployment Discipline"
// (ADR-038's enum-fallback contract): a per-field, registration-time opt-in
// via "x-enum-fallback": true alongside JSON Schema's own standard "enum"
// keyword (EnumFallbackSchemaValidator enforces both are present together,
// and only on a string-typed property). KnownValues is that property's own
// declared "enum" list, carried through so the dynamically-built payload
// field can report whether a since-arrived value is still recognized.
public record EventPayloadProperty(string Name, GraphQlScalarKind Kind, bool IsMaskable, bool EnumFallback, IReadOnlySet<string>? KnownValues);

// Reads a registered EventTypeDefinition's own JsonSchema to drive
// FollowSubscriptionTypeModule's dynamic payload-type construction.
// Deliberately scalar-only -- a top-level "object"/"array"-typed property is
// skipped (not rendered as a GraphQL field at all), an honestly-flagged
// narrowing (08-build-plan.md): every event type this repo's own tests
// actually register uses only scalar top-level properties, so this doesn't
// block any exit criterion, but it is a real, named gap against JSON
// Schema's full expressiveness.
public static class EventTypeSchemaReader
{
    public static IReadOnlyList<EventPayloadProperty> GetTopLevelProperties(string jsonSchemaText)
    {
        if (JsonNode.Parse(jsonSchemaText) is not JsonObject schemaObject || schemaObject["properties"] is not JsonObject properties)
            return [];

        var results = new List<EventPayloadProperty>();
        foreach (var (name, propertyNode) in properties)
        {
            if (propertyNode is not JsonObject propertyObject)
                continue;

            var kind = ResolveKind(propertyObject);
            if (kind is null)
                continue; // object/array-typed property -- not exposed dynamically, see this class's own note

            var isMaskable = propertyObject.ContainsKey("x-masking");
            var enumFallback = propertyObject["x-enum-fallback"]?.GetValue<bool>() ?? false;
            var knownValues = enumFallback && propertyObject["enum"] is JsonArray enumArray
                ? enumArray.Select(v => v?.GetValue<string>()).OfType<string>().ToHashSet()
                : null;
            results.Add(new EventPayloadProperty(name, kind.Value, isMaskable, enumFallback, knownValues));
        }
        return results;
    }

    private static GraphQlScalarKind? ResolveKind(JsonObject propertyObject)
    {
        var typeText = propertyObject["type"]?.GetValue<string>();
        var format = propertyObject["format"]?.GetValue<string>();
        return typeText switch
        {
            "string" => format == "date-time" ? GraphQlScalarKind.DateTimeOffset : GraphQlScalarKind.String,
            "number" or "integer" => GraphQlScalarKind.Float,
            "boolean" => GraphQlScalarKind.Boolean,
            _ => null,
        };
    }
}
