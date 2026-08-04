using System.Text.Json.Nodes;
using EventStore.Persistence;

namespace EventStore.Router;

// ADR-021 -- resolves the uniqueId half of {appId}:{entityType}:{uniqueId}
// from a payload via the registered EntityIdField (e.g. "$.OrderId").
// Reuses JsonPathValidation's own safe-subset walker (FilterableField's
// same restricted dotted-identifier-chain grammar) rather than a second,
// parallel JSON-path implementation.
public static class EntityIdResolver
{
    public static string? ResolveUniqueId(JsonNode? payload, string entityIdField)
    {
        if (string.IsNullOrEmpty(entityIdField) || !JsonPathValidation.IsSafe(entityIdField))
            return null;

        var current = payload;
        foreach (var segment in JsonPathValidation.Segments(entityIdField))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current) || current is null)
                return null;
        }

        return current switch
        {
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonValue value => value.ToJsonString(),
            _ => null, // an object/array can't be a scalar uniqueId
        };
    }
}
