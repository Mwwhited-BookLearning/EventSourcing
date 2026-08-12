using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Follow.Api;

// One continuous poll loop drives both mode=tail (default) and mode=replay
// (ADR-010) -- only lastSeen's initial value differs at the call site
// (docs/06-solution-structure.md, "Follow: tail vs replay cursor").
public class EventTailReader(
    EventStoreContext db, SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker,
    UpcastChain upcastChain, DowncastChain downcastChain)
{
    public async IAsyncEnumerable<FollowedEvent> TailAsync(
        string eventTypeName,
        Expression<Func<StoredEvent, bool>> predicate,
        long lastSeen,
        int? asOfSchemaVersion,
        TimeSpan pollInterval,
        ClaimsPrincipal user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // ADR-027 -- "consuming only originals and always upcasting live
            // remains equally correct" is this design's DEFAULT, not an
            // opt-in; a materialization is a reshaped COPY of an original
            // already delivered once, so surfacing it too would double-
            // deliver one logical fact as two distinct events.
            var matching = await db.Events
                .AsNoTracking()
                .Where(e => e.EventType == eventTypeName && e.EventKind == EventKind.Original && e.SequenceNumber > lastSeen)
                .Where(predicate)
                .OrderBy(e => e.SequenceNumber)
                .ToListAsync(ct);

            if (matching.Count > 0)
            {
                // ADR-018 -- a mode=replay burst can span every version this type
                // has ever had; the destination is always the CURRENT active
                // version, not whichever version happens to appear in this batch,
                // so every intermediate hop's own definition is fetched too, not
                // just the versions literally present among these events.
                var activeDefinition = await schemaRegistry.GetActiveDefinitionByNameAsync(eventTypeName, ct);
                var activeVersion = activeDefinition?.Version ?? matching.Max(e => e.SchemaVersion);
                var minVersion = Math.Min(matching.Min(e => e.SchemaVersion), asOfSchemaVersion ?? activeVersion);
                var maxVersion = Math.Max(activeVersion, asOfSchemaVersion ?? activeVersion);
                var versionsNeeded = Enumerable.Range(minVersion, Math.Max(1, maxVersion - minVersion + 1)).ToList();
                var schemasByVersion = await schemaRegistry.GetVersionsByNameAsync(eventTypeName, versionsNeeded, ct);

                // ADR-028 -- the shape ultimately served to the caller is the
                // requested (asOfSchemaVersion) shape when one was asked for,
                // never the active one -- FollowService.ConnectAsync already
                // confirmed every hop down to it exists before this loop starts.
                var targetVersion = asOfSchemaVersion ?? activeVersion;

                foreach (var storedEvent in matching)
                {
                    var visibleParentIds = await GetVisibleParentEventIdsAsync(storedEvent.EventId, user, ct);
                    var upcastPayload = UpcastPayload(storedEvent, activeVersion, schemasByVersion);
                    var currentPayload = DowncastPayload(upcastPayload, activeVersion, targetVersion, schemasByVersion);
                    var maskedPayload = await MaskPayloadAsync(currentPayload, targetVersion, schemasByVersion, storedEvent.EntityId, user, ct);
                    yield return new FollowedEvent(storedEvent, visibleParentIds, maskedPayload);
                    lastSeen = storedEvent.SequenceNumber;
                }
            }

            if (matching.Count == 0)
                await Task.Delay(pollInterval, ct);
        }
    }

    // ADR-018 -- applied before masking's own transform (ADR-009), per that
    // item's own ordering note: masking must see the payload already reshaped
    // into the active version's fields, not the original stored shape, or its
    // x-masking annotations (declared against the active schema) would be
    // checked against the wrong field names entirely. Fails open to the
    // original, non-upcasted payload if a hop fails -- ADR-020's publish-time
    // validation is what actually prevents a *newly* broken hop; a read-time
    // failure here (e.g. a hop no lagging publish has ever exercised) drops
    // the caller back to the stored shape rather than losing the event.
    private JsonNode UpcastPayload(StoredEvent storedEvent, int activeVersion, IReadOnlyDictionary<int, EventTypeDefinition> schemasByVersion)
    {
        var payloadNode = JsonNode.Parse(storedEvent.Payload)!;
        if (storedEvent.SchemaVersion >= activeVersion)
            return payloadNode;

        var definitionsByVersion = schemasByVersion.ToDictionary(
            kv => kv.Key, kv => new UpcastableVersion(kv.Value.Version, kv.Value.UpcastFromPrevious));
        var outcome = upcastChain.Apply(definitionsByVersion, storedEvent.SchemaVersion, activeVersion, payloadNode);
        return outcome is UpcastOutcome.Success success ? success.Payload : payloadNode;
    }

    // ADR-028 -- applied after UpcastPayload's own reshape, walking backward
    // from the active version to the caller's requested (older) targetVersion.
    // FollowService.ConnectAsync already confirmed every hop has a registered
    // DowncastToPrevious before this reader's loop ever starts; a hop that
    // still fails to evaluate against this particular payload's real data
    // fails open to the upcasted (active-shape) payload, the same read-time
    // posture UpcastPayload itself already takes for its own hop failures.
    private JsonNode DowncastPayload(
        JsonNode upcastPayload, int activeVersion, int targetVersion, IReadOnlyDictionary<int, EventTypeDefinition> schemasByVersion)
    {
        if (targetVersion >= activeVersion)
            return upcastPayload;

        var definitionsByVersion = schemasByVersion.ToDictionary(
            kv => kv.Key, kv => new DowncastableVersion(kv.Value.Version, kv.Value.DowncastToPrevious));
        var outcome = downcastChain.Apply(definitionsByVersion, activeVersion, targetVersion, upcastPayload);
        return outcome is UpcastOutcome.Success success ? success.Payload : upcastPayload;
    }

    // ADR-009 -- the transform is a pure (schema, data, hasClaim) -> data
    // function; hasClaim reuses ADR-008's own "type:value" claim-checking
    // primitive (RequiredClaimEvaluator.HasClaim), deliberately, per that
    // ADR's own "the two features share one claim-checking primitive" text.
    // Masks against the active version's own schema, matching whichever shape
    // currentPayload is actually in after UpcastPayload above -- not
    // storedEvent.SchemaVersion's schema, which may no longer match.
    private async Task<JsonNode?> MaskPayloadAsync(
        JsonNode currentPayload, int activeVersion, IReadOnlyDictionary<int, EventTypeDefinition> schemasByVersion,
        string? entityId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!schemasByVersion.TryGetValue(activeVersion, out var definition))
            return currentPayload;

        var schemaNode = JsonNode.Parse(definition.JsonSchema)!;
        return await payloadMasker.MaskAsync(schemaNode, currentPayload, entityId, claim => RequiredClaimEvaluator.HasClaim(user, claim), ct);
    }

    // ADR-008 -- a restricted parent's ID is omitted from the envelope without
    // blocking the event itself from streaming. A dangling (Permissive-mode,
    // never-resolved) parent reference has no EventType to check against, so
    // it's included unchanged -- "restricted" and "unresolved" are distinct
    // concepts, same distinction Lineage's own resolved/restricted flags make.
    private async Task<IReadOnlyList<Guid>> GetVisibleParentEventIdsAsync(Guid eventId, ClaimsPrincipal user, CancellationToken ct)
    {
        var parentIds = await db.EventParents
            .AsNoTracking()
            .Where(p => p.ChildEventId == eventId)
            .Select(p => p.ParentEventId)
            .ToListAsync(ct);
        if (parentIds.Count == 0)
            return [];

        var resolvedParents = await db.Events
            .AsNoTracking()
            .Where(e => parentIds.Contains(e.EventId))
            .Select(e => new { e.EventId, e.AppId, e.EventType })
            .ToListAsync(ct);
        var parentById = resolvedParents.ToDictionary(e => e.EventId, e => (e.AppId, e.EventType));

        var claimsByKey = await schemaRegistry.GetActiveClaimsByAppAndNamesAsync(
            resolvedParents.Select(e => (e.AppId, e.EventType)).Distinct().ToList(), ct);

        return parentIds.Where(id =>
            !parentById.TryGetValue(id, out var key) ||
            !claimsByKey.TryGetValue(key, out var claims) ||
            RequiredClaimEvaluator.HasAny(claims, ClaimDirection.Read, user)
        ).ToList();
    }
}

public record FollowedEvent(StoredEvent Event, IReadOnlyList<Guid> VisibleParentEventIds, JsonNode? MaskedPayload);
