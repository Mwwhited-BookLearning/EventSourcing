using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Router;

// ADR-027 -- persists a successful lagging-publish upcast as its own
// UpcastMaterialization row, "published through the same append path as
// any other event" (this ADR's own text) -- via EventAppender directly,
// not PublishService.PublishAsync: a materialization is an internally
// generated reshape of an event that already passed its own claims/parent
// checks once, at its own original publish time, not a fresh external
// submission that should be re-gated by them (an empty system principal
// would otherwise be wrongly Forbidden from materializing a claim-gated
// type). Two triggers call into the same TryMaterializeAsync: RouterWorker
// itself (Trigger 1, publish-time) and ReconcileBacklogAsync below
// (Trigger 2, background reconciliation of events already in the log
// before a mapping existed).
public static class UpcastMaterializer
{
    // Returns true if a materialization was created; false if the upcast
    // failed (a hop didn't parse/evaluate, or the result doesn't validate
    // against the active schema) -- never throws for an ordinary upcast
    // failure, matching EventTailReader's own "fail open" posture for the
    // live-read-time equivalent.
    public static async Task<bool> TryMaterializeAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain,
        StoredEvent original, EventTypeDefinition activeDefinition, CancellationToken ct)
    {
        var versionsNeeded = Enumerable.Range(original.SchemaVersion + 1, activeDefinition.Version - original.SchemaVersion).ToList();
        var schemasByVersion = await schemaRegistry.GetVersionsAsync(original.AppId, original.EventType, versionsNeeded, ct);
        var definitionsByVersion = schemasByVersion.ToDictionary(
            kv => kv.Key, kv => new UpcastableVersion(kv.Value.Version, kv.Value.UpcastFromPrevious));

        var payloadNode = JsonNode.Parse(original.Payload)!;
        var outcome = upcastChain.Apply(definitionsByVersion, original.SchemaVersion, activeDefinition.Version, payloadNode);
        if (outcome is not UpcastOutcome.Success success)
            return false;

        var errors = new List<string>();
        var activeSchemaNode = JsonNode.Parse(activeDefinition.JsonSchema);
        if (!JsonSchemaInstanceValidator.Validate(activeSchemaNode, success.Payload, errors))
            return false;

        var payloadJson = success.Payload.ToJsonString();
        var materialization = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            AppId = original.AppId,
            EntityId = original.EntityId, // the same entity -- this is a reshaped copy of the original, not a new fact
            EventType = original.EventType,
            SchemaVersion = activeDefinition.Version,
            EventKind = EventKind.UpcastMaterialization,
            MaterializationOfEventId = original.EventId,
            Payload = payloadJson,
            PayloadHash = EventPayloadHash.Compute(original.EventType, payloadJson, []),
            ChainHash = "", // computed by EventAppender, once SequenceNumber is known
            // Already fully validated above -- a materialization never sits in
            // "received" limbo waiting for the Router to catch up with it.
            Status = "applied",
            SchemaStatus = "conformant",
            OccurredAt = original.OccurredAt,
            ActorId = original.ActorId,
        };

        await EventAppender.AppendAsync(db, materialization, [], ct);
        return true;
    }

    // Trigger 2 -- catches up events already in the log at a version older
    // than the active one, from before a mapping existed for that gap
    // (publish-time materialization, Trigger 1, only ever covers *future*
    // lagging publishes). Re-scans every tick rather than reacting to a
    // registration event directly (no pub/sub mechanism exists for that in
    // this design) -- functionally equivalent, since it eventually catches
    // everything regardless of exactly when a mapping appeared, at the
    // accepted cost ADR-027's own Consequences already names: "no
    // batching/pacing guarantee."
    public static async Task ReconcileBacklogAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain, CancellationToken ct)
    {
        var activeMultiVersionTypes = await db.EventTypeDefinitions
            .Where(e => e.IsActive && e.Version > 1)
            .Select(e => new { e.AppId, e.Name, e.Version })
            .ToListAsync(ct);

        foreach (var activeType in activeMultiVersionTypes)
        {
            var candidates = await db.Events
                .Where(e => e.AppId == activeType.AppId && e.EventType == activeType.Name &&
                            e.EventKind == EventKind.Original && e.SchemaStatus == "conformant" &&
                            e.SchemaVersion < activeType.Version)
                .ToListAsync(ct);
            if (candidates.Count == 0)
                continue;

            var candidateIds = candidates.Select(c => c.EventId).ToList();
            var alreadyMaterializedIds = await db.Events
                .Where(e => e.EventKind == EventKind.UpcastMaterialization && e.MaterializationOfEventId != null &&
                            candidateIds.Contains(e.MaterializationOfEventId!.Value))
                .Select(e => e.MaterializationOfEventId!.Value)
                .ToListAsync(ct);
            var alreadyMaterializedSet = alreadyMaterializedIds.ToHashSet();

            var activeDefinition = await schemaRegistry.GetActiveAsync(activeType.AppId, activeType.Name, ct);
            if (activeDefinition is null)
                continue;

            foreach (var candidate in candidates.Where(c => !alreadyMaterializedSet.Contains(c.EventId)))
                await TryMaterializeAsync(db, schemaRegistry, upcastChain, candidate, activeDefinition, ct);
        }
    }
}
