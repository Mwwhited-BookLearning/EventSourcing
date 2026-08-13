using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Abstractions;
using EventStore.Domain.AccessLog;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Domain.Streaming;
using EventStore.Erasure;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.WorkerWakeSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Inbox;

// ADR-023 -- the "Inbox" half of the Inbox/Router split: auth, idempotency,
// and the two remaining genuinely-blocking checks (unregistered event type,
// unresolved Strict-mode parent), then an unconditional append. Schema
// validation, upcast checking, and entity resolution all move to the async
// Router (EventStore.Router) -- this class never rejects on content anymore,
// only on "there is nothing to persist against" or "the caller may not call
// this at all." ADR-057's crypto-shredding is the one exception to "content
// is the Router's problem": encryption of x-masking.regulatoryClassification
// fields has to happen HERE, synchronously, before Payload is persisted and
// hashed (that ADR's own explicit ordering requirement) -- PayloadEncryptor's
// own comment explains why that means this class independently resolves
// EntityId too, via the same EntityIdResolver utility the Router uses,
// without changing StoredEvent.EntityId's own "starts empty, Router fills
// it in" contract.
public class PublishService(
    EventStoreContext db,
    SchemaRegistryService schemaRegistry,
    IUniqueConstraintViolationDetector uniqueConstraintViolationDetector,
    PayloadEncryptor? payloadEncryptor = null,
    IOptions<OriginIdOptions>? originIdOptions = null,
    ITimestampAuthorityClient? timestampAuthorityClient = null,
    IWorkerWakeSignal? wakeSignal = null,
    ILogger<PublishService>? logger = null)
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

        // ADR-057 -- computed once, up front, and used everywhere request.Payload
        // would otherwise appear below: PayloadHash (both the idempotency
        // short-circuit's comparison basis immediately below AND the stored
        // event's own hash) must be computed over the SAME (post-encryption)
        // representation every time, or a legitimate idempotent retry of a
        // publish containing a classified field would hash differently from
        // what's already stored and be wrongly reported as a 409 Conflict.
        var payloadJson = await EncryptClassifiedFieldsAsync(request, activeDefinition, ct);

        // ADR-011 -- the eventId short-circuit happens before the parent-link
        // check, immediately after confirming the event type is registered.
        if (request.EventId is { } suppliedEventId)
        {
            var existing = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == suppliedEventId, ct);
            if (existing is not null)
                return ReplayOrConflict(existing, ComputeHash(normalizedName, payloadJson, parentEventIds));
        }

        // ADR-064 -- the verified token subject, for EVERY publish, not just
        // a self-attested one; AccessLogReaderContext.Resolve's own claim
        // lookup is reused verbatim (same JWT, same claims, same
        // JwtBearer-vs-TicketAuthenticationHandler "sub" naming quirk its
        // own comment explains) rather than duplicated a second time. Hoisted
        // ahead of the claims/step-up checks below (originally computed only
        // once StoredEvent was built) so a rejection can log WHO was
        // rejected, not just that a rejection happened.
        var actorId = AccessLogReaderContext.Resolve(user).ReaderActorId;

        // ADR-008/050 -- checked against the ACTIVE version's claims, not the
        // caller's declared schemaVersion (which, under ADR-023, might not
        // even exist as a registered version) -- the same active-version
        // fallback Follow/Lineage's own bare-name read-side checks already use
        // (docs/10-open-questions.md row 1).
        if (!RequiredClaimEvaluator.HasAny(activeDefinition.RequiredClaims, ClaimDirection.Publish, user))
        {
            // ADR-050 -- the static log-redaction shape: [ActorIdentity]
            // on PublishServiceLogMessages.PublishRejected's own actorId
            // parameter redacts it before this ever reaches a log sink.
            logger?.PublishRejected(normalizedName, actorId, "missing required claim");
            return new PublishResult.Forbidden();
        }

        // ADR-066 -- RFC 9470 step-up: a signature-required type short-
        // circuits before storage on insufficient authentication strength,
        // the same "real, distinguishable rejection" posture the claims
        // check above already has -- never a content/shape rejection,
        // which ADR-023 still forbids. Checked against the ACTIVE
        // definition, same reasoning as RequiredClaims above.
        string? acr = null;
        if (activeDefinition.RequiredSignature is { } requiredSignature)
        {
            acr = StepUpEvaluator.ResolveAcr(user);
            if (!StepUpEvaluator.IsSatisfied(user, requiredSignature, acr))
            {
                logger?.PublishRejected(normalizedName, actorId, "insufficient step-up authentication");
                return new PublishResult.StepUpRequired(requiredSignature.AcrValues, requiredSignature.MaxAge);
            }
            if (string.IsNullOrWhiteSpace(request.Meaning))
                return new PublishResult.MissingSignatureMeaning();
        }

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

        var payloadHash = ComputeHash(normalizedName, payloadJson, parentEventIds);
        var eventId = request.EventId ?? Guid.NewGuid();

        // ADR-042 -- AuthorityStatus defaults to "accepted" for an ordinary,
        // already-authenticated publish (ADR-006 already verified identity/
        // permission synchronously, nothing left to review). It only starts
        // lower when the publish itself declares a reason not to trust it
        // yet: self-attested credentials (ADR-036, "unattested" -- an
        // identity claim, not yet reviewed at all) or an explicit review-
        // pending marker a detector uses to flag its own unconfirmed output
        // ("pending_review" -- a content/confidence case, not an identity
        // one). The two triggers are deliberately distinguishable in the
        // starting state, even though both feed the same lifecycle onward.
        var authorityStatus =
            request.AttestedActorId is not null || request.AttestedClaims is not null ? "unattested" :
            request.ReviewPending ? "pending_review" :
            "accepted";

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
            Payload = payloadJson,
            PayloadHash = payloadHash,
            ChainHash = "", // computed below, once SequenceNumber is known
            Status = "received", // ADR-023 -- the Router advances this to "applied" asynchronously
            SchemaStatus = null, // advisory, set by the Router once schema validation runs
            ConflictFlag = false, // set by the Router's fold step (ADR-024), never at publish time
            LateArrivalFlag = false, // set by the Router's fold step (ADR-029), never at publish time
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            AttestedActorId = request.AttestedActorId, // ADR-035 -- a CLAIM, never conflated with ActorId above
            AttestedClaims = request.AttestedClaims?.ToJsonString(),
            AuthorityStatus = authorityStatus,
            // ADR-066 -- set only when this type actually required a
            // sign-off; SignerId denormalizes ActorId above (kept explicit
            // rather than implied), Acr records which authentication
            // context the sign-off was actually performed under.
            Signature = activeDefinition.RequiredSignature is not null
                ? new Signature { SignerId = actorId, SignedAt = DateTimeOffset.UtcNow, Meaning = request.Meaning!, Acr = acr! }
                : null,
            DerivationHopCount = derivationHopCount, // 0 for every ordinary publish; only DerivationWorker ever supplies non-zero (ADR-007, deferred)
            // ADR-031/081 -- a detector's TelemetryPointer, JSON-serialized onto the
            // same plain-text envelope column every other structured metadata field uses.
            TelemetryPointer = request.TelemetryPointer is { Count: > 0 } pointer
                ? JsonSerializer.Serialize(pointer, (JsonSerializerOptions?)null)
                : null,
            RespondsToEventId = request.RespondsToEventId, // ADR-094 -- not existence-validated, unlike parentEventIds above
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

        // ADR-095 -- best-effort, AFTER the durable append above genuinely
        // succeeded, never before: each worker's own poll loop is what
        // actually finds and processes this event regardless, so a signal
        // failure here must never fail the publish itself. null wakeSignal
        // (every pre-existing 3-6-arg test construction site) is simply a
        // no-op -- this ADR's own worker-side change is unaffected either
        // way. Every new event is a candidate for all four of these topics
        // (an arbitrary derivation source, peer-sync push, or tracked
        // request/response) -- unlike WebhookOutboxPump's own topic
        // (notified narrowly by RouterWorker, once its fold actually
        // enqueues something), there is no cheaper-to-check condition here
        // than "a new event exists at all," so all four fire together.
        if (wakeSignal is not null)
        {
            await wakeSignal.NotifyAsync(RouterWorker.Topic, ct);
            await wakeSignal.NotifyAsync(WakeSignalTopics.Derivation, ct);
            await wakeSignal.NotifyAsync(WakeSignalTopics.ExpectedResponse, ct);
            await wakeSignal.NotifyAsync(WakeSignalTopics.PeerSync, ct);
        }

        // ADR-086 -- opt-in per event type (RequiredSignature.EnableRfc3161Timestamp),
        // never global. Necessarily AFTER EventAppender.AppendAsync: ChainHash isn't
        // known until that call assigns this row's own SequenceNumber, and
        // EventChainHash.Compute already folds Signature (SignerId/SignedAt/Meaning/
        // Acr) into ChainHash -- RFC3161Timestamp is added as a pure additive update
        // to the SAME Signature afterward, timestamping the chain value that was
        // computed WITHOUT it, never a circular "ChainHash commits to its own
        // timestamp" dependency. "A hash of the signed event's ChainHash" (ADR-086's
        // own Decision text) -- SHA-256 over the ChainHash hex string's UTF-8 bytes,
        // not the ChainHash's own already-hex-decoded bytes (ChainHash is a hash OF
        // prior state, not "the event" itself; hashing it again is what the ADR's
        // wording literally asks for).
        if (storedEvent.Signature is { } signature && activeDefinition.RequiredSignature?.EnableRfc3161Timestamp == true)
        {
            var hashOfChainHash = SHA256.HashData(Encoding.UTF8.GetBytes(storedEvent.ChainHash));
            var timestampToken = await timestampAuthorityClient!.TimestampHashAsync(hashOfChainHash, ct);
            // A NEW Signature instance, never an in-place mutation of the
            // tracked one -- JsonValueConverter.NullableComparer<T>()'s own
            // documented gap: its snapshot function returns the same
            // reference, so EF's change tracker never notices an in-place
            // edit to an already-tracked converted object, only a
            // reassignment (this exact class of bug already found once
            // before, per that file's own comment).
            storedEvent.Signature = new Signature { SignerId = signature.SignerId, SignedAt = signature.SignedAt, Meaning = signature.Meaning, Acr = signature.Acr, RFC3161Timestamp = timestampToken };
            await db.SaveChangesAsync(ct);
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

    // ADR-057 -- resolves EntityId the same way the Router will (activeDefinition's
    // EntityIdField/EntityType, never the declared version's -- ADR-021's identity
    // resolution is a per-event-TYPE decision, stable across versions), then walks
    // the DECLARED version's schema for x-masking.regulatoryClassification leaves
    // (the version RouterWorker's own fold-time validation will check the STORED
    // result against, so encryption and that later validation must agree on which
    // schema they're each looking at). An unregistered declared version, or an
    // unresolvable EntityId, means nothing to encrypt -- returns the payload
    // unchanged, same as PayloadEncryptor's own no-op path.
    private async Task<string> EncryptClassifiedFieldsAsync(PublishEventRequest request, EventTypeDefinition activeDefinition, CancellationToken ct)
    {
        // ADR-057's own encryption machinery is opt-in via DI, same reasoning
        // as _originId above -- every pre-"GDPR/CCPA Erasure" test file (~37
        // of them) constructs this class directly with no PayloadEncryptor at
        // all, and none of them ever register an x-masking.regulatoryClassification
        // field, so there is nothing for a real one to do differently. A real
        // Host always supplies a real, DI-resolved PayloadEncryptor.
        if (payloadEncryptor is null)
            return request.Payload;

        var declaredDefinition = await schemaRegistry.GetVersionAsync(request.AppId, activeDefinition.Name, request.SchemaVersion, ct);
        if (declaredDefinition is null)
            return request.Payload;

        var payloadNode = JsonNode.Parse(request.Payload);
        var uniqueId = EntityIdResolver.ResolveUniqueId(payloadNode, activeDefinition.EntityIdField);
        var entityId = uniqueId is null ? null : $"{request.AppId}:{activeDefinition.EntityType}:{uniqueId}";

        var schemaNode = JsonNode.Parse(declaredDefinition.JsonSchema);
        var encrypted = await payloadEncryptor.EncryptClassifiedFieldsAsync(schemaNode, payloadNode, request.AppId, entityId, ct);
        return encrypted?.ToJsonString() ?? request.Payload;
    }

    private static string ComputeHash(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds) =>
        EventPayloadHash.Compute(eventType, payloadJson, parentEventIds);
}
