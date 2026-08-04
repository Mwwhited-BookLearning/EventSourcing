using System.Text.Json.Nodes;

namespace EventStore.Projections.Host;

// A local enum, deliberately not a reference to EventStore.Domain's own
// ChangeKind -- ProjectionHost's only contact with the write side is HTTP
// JSON (the new GET /registry/{eventType}/change-kind endpoint returns this
// as a plain string), so it parses its own copy rather than sharing a CLR
// type across the write/read boundary the project-reference graph otherwise
// keeps hard (docs/06-solution-structure.md).
public enum ChangeKind { Full, Partial }

// docs/09-cqrs-read-models.md's own sketch, verbatim -- the pre-ADR-022
// whole-payload merge rule (build-plan item 10's own explicit scope: no
// Optional<T> per-property patches, no explicit-null-clears-a-field, both
// later revisions). Applied once, centrally, per ADR-016: Full replaces a
// key's whole snapshot; Partial overwrites only the keys actually PRESENT
// in the incoming payload (RFC 7396's overwrite-if-present half only) --
// a key present with value null still overwrites to null (an ordinary
// value, not a delete signal); a key absent from the payload entirely is
// left untouched in the existing snapshot.
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
