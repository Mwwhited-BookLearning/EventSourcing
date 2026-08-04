using System.Data;
using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Inbox;

public class PublishService(
    EventStoreContext db,
    SchemaRegistryService schemaRegistry,
    IUniqueConstraintViolationDetector uniqueConstraintViolationDetector,
    UpcastChain upcastChain)
{
    // ADR-020 -- the first system-owned event type: reserved at the platform
    // level, never registered through PUT /registry/{event-type} by an
    // operator, so it's special-cased here rather than looked up against
    // EventTypeDefinitions like every other type.
    public const string EventUpcastFailedEventType = "eventupcastfailed";

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

        // ADR-020 -- a declared schemaVersion behind the active version is run
        // through UpcastChain right now, against this real, just-validated
        // payload, as a live compatibility check -- the same chain a Follow/
        // ProjectionHost reader would apply. Deliberately no synthetic-data
        // check at registration time (ADR-018/020 both say so): a hop nobody
        // has published against yet has no observable behavior to validate.
        var activeDefinition = await schemaRegistry.GetActiveAsync(request.AppId, normalizedName, ct);
        if (activeDefinition is not null && request.SchemaVersion < activeDefinition.Version)
        {
            var failure = await CheckUpcastCompatibilityAsync(normalizedName, request, activeDefinition, ct);
            if (failure is not null)
            {
                var deadLetter = await PublishUpcastFailedAsync(normalizedName, request, failure, ct);
                return new PublishResult.Created(deadLetter.EventId, deadLetter.SequenceNumber, deadLetter.SchemaVersion, EventUpcastFailedEventType);
            }
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
            ChainHash = "", // computed below, once SequenceNumber is known
            Status = "applied", // this build stage validates fully synchronously; ADR-023's advisory Status split isn't built yet
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "unauthenticated", // "Auth + Orchestration" hasn't landed yet
            DerivationHopCount = derivationHopCount, // 0 for every ordinary publish; only DerivationWorker ever supplies non-zero (ADR-007, deferred)
        };

        try
        {
            await InsertEventAsync(storedEvent, parentEventIds, ct);
        }
        catch (DbUpdateException ex) when (uniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex, nameof(StoredEvent.EventId)))
        {
            // Lost the race (ADR-011) -- someone else's insert for this EventId committed
            // first. Detach our failed entities so this context can be reused for the lookup.
            db.ChangeTracker.Clear();
            var winner = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId, ct);
            return ReplayOrConflict(winner, payloadHash);
        }

        return new PublishResult.Created(storedEvent.EventId, storedEvent.SequenceNumber, storedEvent.SchemaVersion, normalizedName);
    }

    // ADR-020's compatibility check: every hop from the declared version up to
    // the active one must parse, evaluate, AND the final result must validate
    // against the active version's own schema -- UpcastChain.Apply alone only
    // covers the first two; the schema-validation pass here is what confirms
    // the *output* is actually usable, not just that the expressions ran.
    // Returns null when compatible, or the failure descriptor to dead-letter.
    private async Task<UpcastFailure?> CheckUpcastCompatibilityAsync(
        string normalizedName, PublishEventRequest request, EventTypeDefinition activeDefinition, CancellationToken ct)
    {
        var versionsNeeded = Enumerable.Range(request.SchemaVersion + 1, activeDefinition.Version - request.SchemaVersion).ToList();
        var schemasByVersion = await schemaRegistry.GetVersionsAsync(request.AppId, normalizedName, versionsNeeded, ct);
        var definitionsByVersion = schemasByVersion.ToDictionary(
            kv => kv.Key, kv => new UpcastableVersion(kv.Value.Version, kv.Value.UpcastFromPrevious));

        var payloadNode = JsonNode.Parse(request.Payload)!;
        var outcome = upcastChain.Apply(definitionsByVersion, request.SchemaVersion, activeDefinition.Version, payloadNode);
        if (outcome is UpcastOutcome.Failed failed)
            return new UpcastFailure(failed.FailedAtVersion, failed.Reason);

        var upcasted = ((UpcastOutcome.Success)outcome).Payload;
        var errors = new List<string>();
        var activeSchemaNode = JsonNode.Parse(activeDefinition.JsonSchema);
        JsonSchemaInstanceValidator.Validate(activeSchemaNode, upcasted, errors);
        return errors.Count > 0
            ? new UpcastFailure(activeDefinition.Version, "upcasted result does not satisfy the active schema: " + string.Join(" | ", errors))
            : null;
    }

    // Stores the reserved EventUpcastFailed dead-letter in the original
    // event's place. Carries the original eventType/schemaVersion/payload
    // verbatim, plus which hop failed and why -- exactly ADR-020's payload
    // shape, no parent links (never specified, and the original event this
    // stands in for was never itself resolvable as a parent for anything).
    // Uses a fresh EventId rather than the caller's supplied one: this is a
    // new, system-generated event, not the caller's original one, so a retry
    // of a still-failing upcast is not deduplicated -- it dead-letters again
    // each time, an accepted v1 cost matching ADR-020's own "no proactive
    // check, only discovered when actually hit" posture.
    private async Task<StoredEvent> PublishUpcastFailedAsync(
        string originalEventType, PublishEventRequest request, UpcastFailure failure, CancellationToken ct)
    {
        var failurePayload = new JsonObject
        {
            ["eventType"] = originalEventType,
            ["schemaVersion"] = request.SchemaVersion,
            ["payload"] = JsonNode.Parse(request.Payload),
            ["failedAtVersion"] = failure.FailedAtVersion,
            ["reason"] = failure.Reason,
        }.ToJsonString();

        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EntityId = "",
            EventType = EventUpcastFailedEventType,
            SchemaVersion = 1,
            Payload = failurePayload,
            PayloadHash = ComputeHash(EventUpcastFailedEventType, failurePayload, []),
            ChainHash = "",
            Status = "applied",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "unauthenticated",
            DerivationHopCount = 0,
        };

        await InsertEventAsync(storedEvent, [], ct);
        return storedEvent;
    }

    // ADR-019 -- ChainHash needs this row's own SequenceNumber, which isn't
    // known until the insert itself assigns it (an identity column), so this
    // is necessarily a read-prior-hash, insert, then compute-and-update
    // sequence, not one single insert. Serializable isolation prevents a
    // concurrent publisher's own insert from reading the same "prior tail"
    // and producing two rows that both chain off the same predecessor --
    // a real, accepted v1 cost (a serialization conflict surfaces as a
    // thrown exception a caller would need to retry) for a single linear
    // chain under concurrent writers, not designed further here.
    private async Task InsertEventAsync(StoredEvent storedEvent, IReadOnlyList<Guid> parentEventIds, CancellationToken ct)
    {
        db.Events.Add(storedEvent);
        foreach (var parentEventId in parentEventIds)
            db.EventParents.Add(new EventParent { ChildEventId = storedEvent.EventId, ParentEventId = parentEventId });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var priorChainHash = await db.Events
                .AsNoTracking()
                .OrderByDescending(e => e.SequenceNumber)
                .Select(e => e.ChainHash)
                .FirstOrDefaultAsync(ct) ?? EventChainHash.Genesis;

            await db.SaveChangesAsync(ct);

            storedEvent.ChainHash = EventChainHash.Compute(priorChainHash, storedEvent.PayloadHash, storedEvent.SequenceNumber);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static PublishResult ReplayOrConflict(StoredEvent existing, string candidateHash) =>
        existing.PayloadHash == candidateHash
            ? new PublishResult.IdempotentReplay(existing.EventId, existing.SequenceNumber, existing.SchemaVersion)
            : new PublishResult.Conflict();

    private static string ComputeHash(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds) =>
        EventPayloadHash.Compute(eventType, payloadJson, parentEventIds);

    private record UpcastFailure(int FailedAtVersion, string Reason);
}
