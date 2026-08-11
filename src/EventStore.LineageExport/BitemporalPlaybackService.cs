using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.LineageExport;

public record PlaybackResult(string EntityId, long AsOfSequenceNumber, string Data, string Extensions, bool LateArrivalCorrectionShown);

// ADR-068 -- "reconstruct this entity's state as of transaction time T":
// fold only events with SequenceNumber <= T, in ARRIVAL order, no logical-
// time (OccurredAt) correction -- the literal opposite of EntityStoreRow's
// valid-time-corrected fold (ADR-021/029). v1 computes this on demand, no
// new persisted store (ADR-068's own stated scope) -- every call below is
// a fresh fold over however many events qualify, never a cached snapshot.
public class BitemporalPlaybackService(EventStoreContext db, SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker)
{
    public async Task<PlaybackResult?> ReconstructAsync(string entityId, long asOfSequenceNumber, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var events = await db.Events.AsNoTracking()
            .Where(e => e.EntityId == entityId && e.SequenceNumber <= asOfSequenceNumber)
            .OrderBy(e => e.SequenceNumber) // arrival order, never OccurredAt -- ADR-068's own decided rule
            .ToListAsync(ct);
        if (events.Count == 0)
            return null;

        var data = new JsonObject();
        var extensions = new JsonObject();
        var lateArrivalCorrectionShown = false;

        foreach (var ev in events)
        {
            var definition = await schemaRegistry.GetVersionAsync(ev.AppId, ev.EventType, ev.SchemaVersion, ct);
            if (definition is null)
                continue; // unresolvable schema version at this point in history -- ADR-005's own dangling-reference tolerance, skip rather than fail the whole reconstruction

            var payloadNode = JsonNode.Parse(ev.Payload) as JsonObject ?? new JsonObject();
            var schemaNode = JsonNode.Parse(definition.JsonSchema);
            var (known, unknown) = SplitByConformance(schemaNode, payloadNode);

            // Same Full-replaces/Partial-merges rule RouterWorker.FoldLiveAsync
            // already applies for the live view's own arrival-order fold --
            // duplicated here rather than shared cross-project, the same
            // "EntityDataMerger is deliberately duplicated, not referenced,
            // by a read-side project" precedent EventStore.Projections.Host's
            // own SnapshotMerger already established.
            data = definition.ChangeKind == ChangeKind.Full ? (JsonObject)known.DeepClone() : MergePatch(data, known);
            extensions = MergePatch(extensions, unknown);

            if (ev.LateArrivalFlag)
                lateArrivalCorrectionShown = true; // "recovered in place, right here" -- ADR-068's own UI note; the reconstruction already reflects it, this just flags WHEN it happened for the caller/UI to highlight
        }

        // Masking/erasure enforcement identical to any other read (ADR-068) --
        // one pass over the FINAL folded result, per the current caller's own
        // claims, never baked in at fold time (the same "computed fresh per
        // reader" posture EventTailReader's own Follow-stream masking already
        // established -- there is no persisted store here to have baked it
        // into in the first place).
        var maskedData = await MaskFoldedDataAsync(events, data, user, ct);

        return new PlaybackResult(entityId, asOfSequenceNumber, maskedData.ToJsonString(), extensions.ToJsonString(), lateArrivalCorrectionShown);
    }

    private async Task<JsonNode> MaskFoldedDataAsync(List<Domain.EventLog.StoredEvent> events, JsonObject data, ClaimsPrincipal user, CancellationToken ct)
    {
        // Masking config lives on each field's OWN declaring schema version,
        // not on the folded whole -- masks against the entity type's most
        // recently contributing event's own declared schema, the same
        // "most recent wins" precedent ADR-042 already uses for AuthorityStatus
        // roll-up on FoldLiveAsync's own row.
        var last = events[^1];
        var definition = await schemaRegistry.GetVersionAsync(last.AppId, last.EventType, last.SchemaVersion, ct);
        if (definition is null)
            return data;

        var schemaNode = JsonNode.Parse(definition.JsonSchema);
        return await payloadMasker.MaskAsync(schemaNode!, data, last.EntityId, claim => RequiredClaimEvaluator.HasClaim(user, claim), ct) ?? data;
    }

    // Duplicated from RouterWorker.SplitByConformance (internal to that
    // project) -- a small, self-contained JSON-Schema-conformance check;
    // not worth a cross-project internal-visibility grant for one function.
    private static (JsonObject Known, JsonObject Unknown) SplitByConformance(JsonNode? schemaNode, JsonObject payload)
    {
        var known = new JsonObject();
        var unknown = new JsonObject();
        var declaredProperties = schemaNode is JsonObject schemaObject && schemaObject["properties"] is JsonObject props ? props : null;

        foreach (var (name, value) in payload)
        {
            if (declaredProperties is not null && declaredProperties.TryGetPropertyValue(name, out var propertySchema))
            {
                var errors = new List<string>();
                if (value is not null && JsonSchemaInstanceValidator.Validate(propertySchema, value, errors))
                    known[name] = value.DeepClone();
            }
            else
            {
                unknown[name] = value?.DeepClone();
            }
        }
        return (known, unknown);
    }

    // Duplicated from EventStore.Router.EntityDataMerger.MergePatch -- same
    // "duplicated, not shared" precedent as above.
    private static JsonObject MergePatch(JsonNode? current, JsonObject incoming)
    {
        var result = current is JsonObject baseObject ? (JsonObject)baseObject.DeepClone() : new JsonObject();
        foreach (var (key, value) in incoming)
            result[key] = value?.DeepClone();
        return result;
    }
}
