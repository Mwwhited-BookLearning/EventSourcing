using System.Text.Json.Nodes;
using EventStore.Persistence;

namespace EventStore.Erasure;

// erasureScope is a JSON Pointer into the SAME payload's root (ADR-057) --
// reuses JsonPathValidation's own safe-subset walker, the same restricted
// grammar EntityIdResolver already applies to EntityIdField, for the same
// injection-surface reason. Shared between PayloadEncryptor (publish-time)
// and PayloadMasker's own decrypt step (read-time) so both resolve a
// classified field's owning entity identically. This build stage's own
// documented convention for the cross-entity case ADR-057 leaves
// unspecified: the pointed-at value must itself already be a complete
// EntityId string ({appId}:{entityType}:{uniqueId}), not a bare identifier
// a scheme would need to infer an EntityType for.
public static class ErasureScopeResolver
{
    public static string Resolve(JsonNode? rootPayload, JsonObject maskingConfig, string defaultEntityId) =>
        maskingConfig["erasureScope"]?.GetValue<string>() is { } pointer
            ? ResolvePointer(rootPayload, pointer) ?? defaultEntityId
            : defaultEntityId;

    private static string? ResolvePointer(JsonNode? rootPayload, string jsonPathPointer)
    {
        if (!JsonPathValidation.IsSafe(jsonPathPointer))
            return null;

        var current = rootPayload;
        foreach (var segment in JsonPathValidation.Segments(jsonPathPointer))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current) || current is null)
                return null;
        }

        return current is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
    }
}
