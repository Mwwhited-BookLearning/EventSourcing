using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Domain.Streaming;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventStore.Inbox;

// ADR-023 -- the "Inbox" half of the Inbox/Router split: auth, idempotency,
// and the two remaining genuinely-blocking checks (unregistered event type,
// unresolved Strict-mode parent), then an unconditional append. Schema
// validation, upcast checking, and entity resolution all move to the async
// Router (EventStore.Router) -- this class never rejects on content anymore,
// only on "there is nothing to persist against" or "the caller may not call
// this at all."
public class PublishService(
    EventStoreContext db,
    SchemaRegistryService schemaRegistry,
    IUniqueConstraintViolationDetector uniqueConstraintViolationDetector,
    IOptions<OriginIdOptions>? originIdOptions = null)
{
    // ADR-033 -- defaults every existing 3-arg construction site (every
    // pre-"Sharding & Replication" test file, ~26 of them) to a single-
    // site-shaped OriginId rather than forcing a mechanical sweep across
    // all of them for a value most don't care about; a real Host always
    // supplies a real configured value via DI.
    private readonly string _originId = originIdOptions?.Value.OriginId ?? OriginIdOptions.Default;
    public async Task<PublishResult> PublishAsync(
        string eventTypeName, PublishEventRequest request, ClaimsPrincipal user, CancellationToken ct = default, int derivationHopCount = 0)
    {
        var normalizedName = eventTypeName.ToLowerInvariant();
        var parentEventIds = request.ParentEventIds ?? [];

        // ADR-023 -- the one remaining case this posture doesn't cover: an
        // event type never registered under any version at all has no
        // schema/AppId context to even persist against.
        var activeDefinition = await schemaRegistry.GetActiveAsync(request.AppId, normalizedName, ct);
        if (activeDefinition is null)
            return new PublishResult.UnregisteredEventType();

        // ADR-011 -- the eventId short-circuit happens before the parent-link
        // check, immediately after confirming the event type is registered.
        if (request.EventId is { } suppliedEventId)
        {
            var existing = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == suppliedEventId, ct);
            if (existing is not null)
                return ReplayOrConflict(existing, ComputeHash(normalizedName, request.Payload, parentEventIds));
        }

        // ADR-008/050 -- checked against the ACTIVE version's claims, not the
        // caller's declared schemaVersion (which, under ADR-023, might not
        // even exist as a registered version) -- the same active-version
        // fallback Follow/Lineage's own bare-name read-side checks already use
        // (docs/10-open-questions.md row 1).
        if (!RequiredClaimEvaluator.HasAny(activeDefinition.RequiredClaims, ClaimDirection.Publish, user))
            return new PublishResult.Forbidden();

        // ADR-005 -- still real, still blocking (03-api-contracts.md's error
        // table: unaffected by ADR-023). Uses the active version's
        // ParentValidationMode for the same reason as the claims check above.
        if (activeDefinition.ParentValidationMode == ParentValidationMode.Strict && parentEventIds.Count > 0)
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
            AppId = request.AppId, // ADR-021 -- the real AppId source for EntityId's own {appId}:... prefix, closing docs/10-open-questions.md row 1's ambiguity for entity resolution specifically
            EntityId = "", // resolved by the Router once it fold this event (ADR-021)
            OriginId = _originId, // ADR-033 -- every client-facing publish originates AT this site, by definition
            LogicalClock = "", // computed by EventAppender, once the prior clock value is known (mirrors ChainHash)
            EventType = normalizedName,
            SchemaVersion = request.SchemaVersion,
            ExpectedVersion = request.ExpectedVersion,
            Payload = request.Payload,
            PayloadHash = payloadHash,
            ChainHash = "", // computed below, once SequenceNumber is known
            Status = "received", // ADR-023 -- the Router advances this to "applied" asynchronously
            SchemaStatus = null, // advisory, set by the Router once schema validation runs
            ConflictFlag = false, // set by the Router's fold step (ADR-024), never at publish time
            LateArrivalFlag = false, // set by the Router's fold step (ADR-029), never at publish time
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "unauthenticated", // "Auth + Orchestration" doesn't populate this from the token yet
            DerivationHopCount = derivationHopCount, // 0 for every ordinary publish; only DerivationWorker ever supplies non-zero (ADR-007, deferred)
            // ADR-031/081 -- a detector's TelemetryPointer, JSON-serialized onto the
            // same plain-text envelope column every other structured metadata field uses.
            TelemetryPointer = request.TelemetryPointer is { Count: > 0 } pointer
                ? JsonSerializer.Serialize(pointer, (JsonSerializerOptions?)null)
                : null,
        };

        try
        {
            await EventAppender.AppendAsync(db, storedEvent, parentEventIds, ct);
        }
        catch (DbUpdateException ex) when (uniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex, nameof(StoredEvent.EventId)))
        {
            // Lost the race (ADR-011) -- someone else's insert for this EventId committed
            // first. Detach our failed entities so this context can be reused for the lookup.
            db.ChangeTracker.Clear();
            var winner = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId, ct);
            return ReplayOrConflict(winner, payloadHash);
        }

        // ADR-032 -- completes the two-step handoff: POST /attachments already
        // returned this ContentHash; linking it here creates the AttachmentRef
        // row without ever putting the raw bytes in Payload. EntityId is left
        // null -- entity resolution is the async Router's job (ADR-021), not
        // the synchronous Inbox's, the same reason StoredEvent.EntityId itself
        // starts empty above.
        foreach (var contentHash in request.AttachmentContentHashes ?? [])
            db.AttachmentRefs.Add(new AttachmentRef { ContentHash = contentHash, EntityId = null, EventId = storedEvent.EventId });
        if (request.AttachmentContentHashes is { Count: > 0 })
            await db.SaveChangesAsync(ct);

        return ToAccepted(storedEvent);
    }

    // ADR-023's response envelope reflects whatever is already known AT THIS
    // SYNCHRONOUS MOMENT -- null/""/false for a freshly-inserted event (the
    // Router hasn't run yet), or the event's current, already-processed
    // values for an idempotent replay of a request the Router has since
    // caught up with.
    private static PublishResult.Accepted ToAccepted(StoredEvent storedEvent) => new(
        storedEvent.EventId, storedEvent.SequenceNumber, storedEvent.Status, storedEvent.EntityId,
        storedEvent.SchemaStatus, storedEvent.AuthorityStatus, storedEvent.ConflictFlag, Reason: null);

    private static PublishResult ReplayOrConflict(StoredEvent existing, string candidateHash) =>
        existing.PayloadHash == candidateHash
            ? ToAccepted(existing)
            : new PublishResult.Conflict();

    private static string ComputeHash(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds) =>
        EventPayloadHash.Compute(eventType, payloadJson, parentEventIds);
}
