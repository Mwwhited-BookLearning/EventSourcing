using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Router;

// docs/features/non-authoritative-capture.md's "special-purpose reactor"
// shape -- reacts to one event type (authorityDecision), the same pattern
// ADR-020's EventUpcastFailed handling and ADR-027's materialization
// already use, not a new generic fold mechanism. Annotating the target
// event's AuthorityStatus/AuthorityDecisionRef, catching the authoritative
// Entity Store up on acceptance, and emitting a compensating patch on a
// Compensate-type rejection are all side effects performed here, invoked by
// RouterWorker right after an authorityDecision event's own (ordinary)
// fold into its own {appId}:authoritydecision:{targetEventId} entity.
public static class AuthorityDecisionResolver
{
    public static async Task ProcessAsync(EventStoreContext db, SchemaRegistryService schemaRegistry, StoredEvent decisionEvent, CancellationToken ct)
    {
        if (JsonNode.Parse(decisionEvent.Payload) is not JsonObject payload)
            return;

        var targetEventIdText = payload["targetEventId"]?.GetValue<string>();
        var decision = payload["decision"]?.GetValue<string>();
        if (targetEventIdText is null || !Guid.TryParse(targetEventIdText, out var targetEventId) || decision is not ("accepted" or "rejected"))
            return;

        var target = await db.Events.SingleOrDefaultAsync(e => e.EventId == targetEventId, ct);
        if (target is null)
            return; // ADR-005-style tolerance -- a dangling/not-yet-resolvable reference is stored, never blocking

        // ADR-035's Consequences -- "two servers can independently disagree
        // about whether something's been reviewed, resolved the same way as
        // any other divergence (ADR-024's ConflictFlag, reused)": whichever
        // decision is applied SECOND against an already-decided target is
        // flagged on ITSELF, never blocked, and simply overwrites -- last
        // applied wins, no merge.
        if (target.AuthorityDecisionRef is { } existingRef && existingRef != decisionEvent.EventId)
            decisionEvent.ConflictFlag = true;

        var wasAlreadyAccepted = target.AuthorityStatus == "accepted";
        target.AuthorityStatus = decision;
        target.AuthorityDecisionRef = decisionEvent.EventId;

        if (decision == "accepted")
        {
            // ADR-042 -- "once approved, the authoritative Entity Store
            // catches up... the same 'apply once, on the triggering
            // condition' shape ADR-027's materialization catch-up already
            // uses." The Live View already reflected this data; only the
            // authoritative store needs to catch up now.
            await CatchUpAuthoritativeFoldAsync(db, schemaRegistry, target, ct);
        }
        else if (wasAlreadyAccepted)
        {
            // ADR-042's narrowing of the annotate-vs-compensate fork
            // (comparisons/authority-rejection-behavior.md): a rejection of
            // an event that was never accepted has nothing to compensate
            // for -- it simply never applied. Compensate only matters for
            // this residual case: already accepted and folded, now reversed.
            var declaredDefinition = await schemaRegistry.GetVersionAsync(target.AppId, target.EventType, target.SchemaVersion, ct);
            if (declaredDefinition?.RejectionBehavior == RejectionBehavior.Compensate)
                await AppendCompensatingPatchAsync(db, target, declaredDefinition, ct);
        }
        // RejectionBehavior.Annotate (the default): the target's Payload and
        // the authoritative Entity Store are left exactly as they were --
        // AuthorityStatus alone is the flag a consumer must check.
    }

    private static async Task CatchUpAuthoritativeFoldAsync(EventStoreContext db, SchemaRegistryService schemaRegistry, StoredEvent target, CancellationToken ct)
    {
        var (known, unknown, changeKind) = await SplitPayloadAsync(schemaRegistry, target, ct);
        var activeDefinition = await schemaRegistry.GetActiveAsync(target.AppId, target.EventType, ct);
        if (activeDefinition is null)
            return; // shouldn't happen -- target already resolved an EntityId once, under the same active definition

        await RouterWorker.FoldAsync(db, target.EntityId, target, activeDefinition.EntityType, changeKind, known, unknown, ct);
    }

    private static async Task AppendCompensatingPatchAsync(EventStoreContext db, StoredEvent target, EventTypeDefinition declaredDefinition, CancellationToken ct)
    {
        var payloadNode = JsonNode.Parse(target.Payload) as JsonObject ?? new JsonObject();
        var schemaNode = JsonNode.Parse(declaredDefinition.JsonSchema);
        var (known, _) = RouterWorker.SplitByConformance(schemaNode, payloadNode);

        // ADR-022's Specified(null) -- an explicit clear of exactly the
        // properties THIS event contributed, via EntityDataMerger's existing
        // "a key present with a JSON null overwrites to null" rule. Always
        // folded as Partial below, regardless of the target type's own
        // ChangeKind -- a compensating patch's whole job is "clear exactly
        // what this event touched," an inherently partial operation; using
        // the original ChangeKind (e.g. Full) would wipe every OTHER
        // property too, not just this event's own contribution.
        var revertPayload = new JsonObject();
        foreach (var (key, _) in known)
            revertPayload[key] = null;

        var currentVersion = await db.EntityStore
            .Where(r => r.EntityId == target.EntityId)
            .Select(r => (long?)r.Version)
            .SingleOrDefaultAsync(ct);

        var compensatingEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            AppId = target.AppId,
            EntityId = target.EntityId,
            OriginId = target.OriginId,
            LogicalClock = "", // computed by EventAppender, this site's own chain
            EventType = target.EventType,
            SchemaVersion = target.SchemaVersion,
            ExpectedVersion = currentVersion,
            Payload = revertPayload.ToJsonString(),
            PayloadHash = "", // computed below
            ChainHash = "", // computed by EventAppender
            Status = "applied", // system-generated and folded immediately, never left "received" for the Router to reprocess (same posture as UpcastMaterializer)
            SchemaStatus = "conformant", // constructed from an already-conformant target's own known-good properties
            AuthorityStatus = "accepted", // a compensating patch is authoritative by construction (ADR-042) -- not itself subject to review
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system:authority-decision-resolver",
        };
        compensatingEvent.PayloadHash = EventPayloadHash.Compute(compensatingEvent.EventType, compensatingEvent.Payload, []);

        await EventAppender.AppendAsync(db, compensatingEvent, [], ct);

        await RouterWorker.FoldAsync(db, target.EntityId, compensatingEvent, declaredDefinition.EntityType, ChangeKind.Partial, revertPayload, [], ct);
        await RouterWorker.FoldLiveAsync(db, target.EntityId, compensatingEvent, declaredDefinition.EntityType, ChangeKind.Partial, revertPayload, [], ct);
    }

    // Recomputes the same known/unknown split ProcessEventAsync originally
    // made against the target's own declared version -- not persisted
    // anywhere, so recomputed here identically rather than invented fresh.
    private static async Task<(JsonObject Known, JsonObject Unknown, ChangeKind ChangeKind)> SplitPayloadAsync(
        SchemaRegistryService schemaRegistry, StoredEvent target, CancellationToken ct)
    {
        var payloadNode = JsonNode.Parse(target.Payload) as JsonObject ?? new JsonObject();
        var declaredDefinition = await schemaRegistry.GetVersionAsync(target.AppId, target.EventType, target.SchemaVersion, ct);
        if (declaredDefinition is null)
            return ([], (JsonObject)payloadNode.DeepClone(), ChangeKind.Partial);

        var schemaNode = JsonNode.Parse(declaredDefinition.JsonSchema);
        var (known, unknown) = RouterWorker.SplitByConformance(schemaNode, payloadNode);
        return (known, unknown, declaredDefinition.ChangeKind);
    }
}
