using System.Text.Json.Nodes;

namespace EventStore.Router;

// ADR-022's three-state fold rule (Unspecified/Specified(null)/Specified(value)),
// applied directly against JsonNode structures rather than a strongly-typed
// Optional&lt;T&gt; DTO -- a key absent from a JsonObject is never enumerated
// (Unspecified: left untouched), a key present with a JSON null value
// enumerates with a null CLR reference (Specified(null): overwrites to
// null, i.e. Clear), a key present with a value enumerates with that value
// (Specified(value): overwrite). This mirrors EventStore.Projections.Host's
// own SnapshotMerger exactly -- duplicated, not shared, since that project
// deliberately has zero reference to anything on the write side
// (docs/06-solution-structure.md).
public static class EntityDataMerger
{
    public static JsonObject MergePatch(JsonNode? current, JsonObject incoming)
    {
        var result = current is JsonObject baseObject ? (JsonObject)baseObject.DeepClone() : new JsonObject();
        foreach (var (key, value) in incoming)
            result[key] = value?.DeepClone();
        return result;
    }
}
