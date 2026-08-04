using System.Data;
using System.Security.Claims;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

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
    IUniqueConstraintViolationDetector uniqueConstraintViolationDetector)
{
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
            ? ToAccepted(existing)
            : new PublishResult.Conflict();

    private static string ComputeHash(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds) =>
        EventPayloadHash.Compute(eventType, payloadJson, parentEventIds);
}
