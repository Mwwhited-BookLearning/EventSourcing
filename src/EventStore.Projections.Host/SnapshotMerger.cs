using System.Text.Json.Nodes;
using EventStore.Projections.Abstractions;

namespace EventStore.Projections.Host;

// docs/09-cqrs-read-models.md's own sketch, verbatim. Applied once,
// centrally, per ADR-016: Full replaces a key's whole snapshot; Partial
// overwrites only the keys actually PRESENT in the incoming payload
// (RFC 7396's overwrite-if-present half only). Confirmed against ADR-022's
// later, more precise three-state framing ("Entity-Centric Core Rebuild")
// that this JsonNode-based merge already implements it correctly, with no
// logic change needed: `foreach (var (key, value) in patchObject)` only
// ever visits PRESENT keys, so an absent key is Unspecified (left
// untouched) automatically; a present key whose value is JSON null enumerates
// with a null CLR reference, so `result[key] = value` is Specified(null)
// (an explicit Clear); a present key with a real value is Specified(value)
// (Overwrite). ADR-022's actual new content is the Optional<T> C# wrapper
// type for strongly-typed DTOs elsewhere in this design -- this JsonNode
// merge never needed one to already be correct.
public static class SnapshotMerger
{
    public static JsonNode Merge(ChangeKind changeKind, JsonNode? existingSnapshot, JsonNode incomingPayload) =>
        changeKind == ChangeKind.Full ? incomingPayload.DeepClone() : MergePatch(existingSnapshot, incomingPayload);

    private static JsonNode MergePatch(JsonNode? current, JsonNode incoming)
    {
        if (current is not JsonObject baseObject)
            return incoming.DeepClone();
        if (incoming is not JsonObject patchObject)
            return incoming.DeepClone();

        var result = (JsonObject)baseObject.DeepClone();
        foreach (var (key, value) in patchObject)
            result[key] = value?.DeepClone();
        return result;
    }
}
