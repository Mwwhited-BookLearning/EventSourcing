using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Inbox;

public class PublishService(
    EventStoreContext db,
    SchemaRegistryService schemaRegistry,
    IUniqueConstraintViolationDetector uniqueConstraintViolationDetector)
{
    public async Task<PublishResult> PublishAsync(
        string eventTypeName, PublishEventRequest request, ClaimsPrincipal user, CancellationToken ct = default, int derivationHopCount = 0)
    {
        var normalizedName = eventTypeName.ToLowerInvariant();
        var parentEventIds = request.ParentEventIds ?? [];

        var isRegistered = await db.EventTypeDefinitions
            .AnyAsync(e => e.AppId == request.AppId && e.Name == normalizedName, ct);
        if (!isRegistered)
            return new PublishResult.UnregisteredEventType();

        // ADR-011 -- the eventId short-circuit happens before schema/parent-link
        // validation, immediately after confirming the event type is registered.
        if (request.EventId is { } suppliedEventId)
        {
            var existing = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == suppliedEventId, ct);
            if (existing is not null)
                return ReplayOrConflict(existing, ComputeHash(normalizedName, request.Payload, parentEventIds));
        }

        var definition = await schemaRegistry.GetVersionAsync(request.AppId, normalizedName, request.SchemaVersion, ct);
        if (definition is null)
            return new PublishResult.ValidationFailed([$"schemaVersion {request.SchemaVersion} is not a registered version of {eventTypeName}"]);

        // ADR-008/050 -- checked before content validation, same as any other
        // access-control gate; AppId is explicit here (the request's own field),
        // so there's no lookup ambiguity the way Follow/Lineage's bare-EventType
        // read-side checks have (docs/10-open-questions.md row 1).
        if (!RequiredClaimEvaluator.HasAny(definition.RequiredClaims, ClaimDirection.Publish, user))
            return new PublishResult.Forbidden();

        var errors = new List<string>();
        var payloadNode = JsonNode.Parse(request.Payload);
        var schemaNode = JsonNode.Parse(definition.JsonSchema);
        JsonSchemaInstanceValidator.Validate(schemaNode, payloadNode, errors);
        if (errors.Count > 0)
            return new PublishResult.ValidationFailed(errors);

        if (definition.ParentValidationMode == ParentValidationMode.Strict && parentEventIds.Count > 0)
        {
            var resolvedIds = await db.Events
                .Where(e => parentEventIds.Contains(e.EventId))
                .Select(e => e.EventId)
                .ToListAsync(ct);
            var missing = parentEventIds.Except(resolvedIds).ToList();
            if (missing.Count > 0)
                return new PublishResult.UnresolvedParent(missing);
        }

        var payloadHash = ComputeHash(normalizedName, request.Payload, parentEventIds);
        var eventId = request.EventId ?? Guid.NewGuid();

        var storedEvent = new StoredEvent
        {
            EventId = eventId,
            EntityId = "", // not resolved until "Entity-Centric Core Rebuild" (ADR-021)
            EventType = normalizedName,
            SchemaVersion = request.SchemaVersion,
            Payload = request.Payload,
            PayloadHash = payloadHash,
            ChainHash = "", // "Hardening & Evolution"'s job (ADR-019) -- not computed yet
            Status = "applied", // this build stage validates fully synchronously; ADR-023's advisory Status split isn't built yet
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "unauthenticated", // "Auth + Orchestration" hasn't landed yet
            DerivationHopCount = derivationHopCount, // 0 for every ordinary publish; only DerivationWorker ever supplies non-zero (ADR-007, deferred)
        };
        db.Events.Add(storedEvent);

        foreach (var parentEventId in parentEventIds)
            db.EventParents.Add(new EventParent { ChildEventId = eventId, ParentEventId = parentEventId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex, nameof(StoredEvent.EventId)))
        {
            // Lost the race (ADR-011) -- someone else's insert for this EventId committed
            // first. Detach our failed entities so this context can be reused for the lookup.
            db.ChangeTracker.Clear();
            var winner = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId, ct);
            return ReplayOrConflict(winner, payloadHash);
        }

        return new PublishResult.Created(storedEvent.EventId, storedEvent.SequenceNumber, storedEvent.SchemaVersion);
    }

    private static PublishResult ReplayOrConflict(StoredEvent existing, string candidateHash) =>
        existing.PayloadHash == candidateHash
            ? new PublishResult.IdempotentReplay(existing.EventId, existing.SequenceNumber, existing.SchemaVersion)
            : new PublishResult.Conflict();

    private static string ComputeHash(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds)
    {
        // Canonical serialization of { eventType, payload, parentEventIds: <sorted> },
        // SHA-256 (ADR-011) -- key order is hardcoded here rather than derived from any
        // external canonicalization spec, since this hash only ever needs to be
        // internally consistent with itself across retries of the same request.
        var canonical = new JsonObject
        {
            ["eventType"] = eventType,
            ["payload"] = JsonNode.Parse(payloadJson),
            ["parentEventIds"] = new JsonArray(parentEventIds.OrderBy(id => id).Select(id => (JsonNode)id.ToString()).ToArray()),
        };
        var bytes = Encoding.UTF8.GetBytes(canonical.ToJsonString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
