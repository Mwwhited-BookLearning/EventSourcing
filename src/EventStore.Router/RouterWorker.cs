using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EventStore.Domain.EntityStore;
using EventStore.Domain.EventLog;
using EventStore.Domain.Observability;
using EventStore.Domain.SchemaRegistry;
using EventStore.Erasure;
using EventStore.LeaderElection;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using EventStore.Webhooks;
using EventStore.WorkerWakeSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Router;

// ADR-023's "Router" -- the async half of the Inbox/Router split: schema
// validation (advisory SchemaStatus, never blocking), entity resolution
// (ADR-021), and the Entity Store fold itself (ADR-021/022/024/029), all
// running after PublishService (the "Inbox") has already persisted the
// event as Status: "received". One polling loop, same BackgroundService +
// testable static RunOnceAsync shape DerivationWorker already established.
//
// docs/06-solution-structure.md names Router and EventStore.Fold as two
// separate deployables; this build stage combines both responsibilities
// into one project/worker instead -- the same "concept accurate, exact
// wiring unverified" gap that sketch's own banner already covers, applied
// here rather than standing up two near-empty processes for one pass.
public class RouterWorker(IServiceScopeFactory scopeFactory, ILogger<RouterWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    // ADR-095 -- the one topic name PublishService's own NotifyAsync call
    // targets; shared here as a constant so the two ends of this wake
    // signal can never drift apart by a typo.
    public const string Topic = "router";

    // ADR-078 -- "Router" is one of the 4 named worker roles. Its own
    // UpcastMaterializer calls (below, inline in RunOnceAsync) run ONLY as
    // part of this SAME tick, under this SAME lease -- there is no
    // separate, independently-schedulable "UpcastMaterializer" process in
    // this build (see this class's own header comment: Router and
    // UpcastMaterializer were combined into one worker, not two deployables,
    // this build stage). A second lease row for it would protect nothing
    // that this one doesn't already cover, so none is created; documented
    // explicitly rather than silently deviating from ADR-078's literal
    // 4-independent-roles framing.
    private const string WorkerRole = "Router";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);
    // Renewing on every 200ms tick would add a write query to the lease
    // table 5x more often than necessary and, under heavy parallel test
    // load, measurably slowed down the real fold work sharing the same
    // SQLite file -- found only by running this (several HTTP tests'
    // OWN fixed wait margins started missing intermittently). Renewing at
    // the halfway point of the lease's own duration leaves 2-3 retry
    // attempts before it could actually expire, comfortable margin against
    // a single transient failure, while cutting the added overhead by 90%.
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(2.5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isLeader = false;
        var nextRenewalAt = DateTimeOffset.MinValue; // forces an immediate first acquisition attempt
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();

                if (DateTimeOffset.UtcNow >= nextRenewalAt)
                {
                    var leaderElection = scope.ServiceProvider.GetRequiredService<LeaderElectionService>();
                    var acquired = await leaderElection.TryAcquireOrRenewAsync(WorkerRole, LeaseHolderId.Current, LeaseDuration, stoppingToken);
                    if (acquired != isLeader)
                    {
                        isLeader = acquired;
                        logger.LogInformation("Router {State} the {WorkerRole} lease", isLeader ? "acquired" : "lost", WorkerRole);
                    }
                    // A failed attempt retries next TICK (200ms), not next
                    // scheduled renewal -- losing/never having the lease
                    // should recover as fast as this worker's own poll
                    // interval allows, not wait out a stale renewal clock.
                    nextRenewalAt = isLeader ? DateTimeOffset.UtcNow + RenewInterval : DateTimeOffset.MinValue;
                }

                if (isLeader)
                {
                    var schemaRegistry = scope.ServiceProvider.GetRequiredService<SchemaRegistryService>();
                    var upcastChain = scope.ServiceProvider.GetRequiredService<UpcastChain>();
                    var erasureKeyService = scope.ServiceProvider.GetRequiredService<ErasureKeyService>();
                    var payloadMasker = scope.ServiceProvider.GetRequiredService<IPayloadMasker>();
                    await RunOnceAsync(db, schemaRegistry, upcastChain, erasureKeyService, payloadMasker, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A tick failing (a transient DB error, an unanticipated payload
                // shape) must not take the whole worker down -- it retries next
                // tick, same resiliency posture as EventTailReader's own poll loop.
                logger.LogError(ex, "Router tick failed");
            }

            // ADR-095 -- waits up to PollInterval, but returns early the
            // moment PublishService signals new work exists. Never the sole
            // correctness mechanism: a missed/lost signal (the Sqlite
            // implementation's own brief restart window; a genuinely
            // dropped Postgres NOTIFY; a Service Broker message this
            // instance wasn't listening for) just means this wait runs the
            // full PollInterval, exactly the behavior this worker already
            // had before this signal existed. A fresh, short-lived scope,
            // same reasoning the tick's own scope above already follows --
            // IWorkerWakeSignal (scoped) can't be a constructor dependency
            // of this singleton BackgroundService at all (confirmed by
            // DI's own build-time validation refusing to start otherwise).
            try
            {
                using var wakeScope = scopeFactory.CreateScope();
                var wakeSignal = wakeScope.ServiceProvider.GetRequiredService<IWorkerWakeSignal>();
                await wakeSignal.WaitForWakeAsync(Topic, PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    // One tick's worth of work, factored out of ExecuteAsync's loop so tests
    // can drive it directly against a provider-backed context, the same
    // pattern DerivationWorker.RunOnceAsync already established. Returns the
    // number of events processed this tick (received events only -- ADR-027's
    // Trigger 2 backlog reconciliation runs every tick too, but isn't counted
    // here since it's not driven by "received" events at all).
    public static async Task<int> RunOnceAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain, ErasureKeyService? erasureKeyService = null,
        IPayloadMasker? payloadMasker = null, CancellationToken ct = default)
    {
        var received = await db.Events
            .Where(e => e.Status == "received")
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);

        foreach (var storedEvent in received)
            await ProcessEventAsync(db, schemaRegistry, upcastChain, erasureKeyService, payloadMasker, storedEvent, ct);

        if (received.Count > 0)
            await db.SaveChangesAsync(ct);

        // ADR-027 Trigger 2 -- catches up any backlog left by a mapping that
        // didn't exist yet when its events originally folded.
        await UpcastMaterializer.ReconcileBacklogAsync(db, schemaRegistry, upcastChain, ct);

        return received.Count;
    }

    private static async Task ProcessEventAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain, ErasureKeyService? erasureKeyService,
        IPayloadMasker? payloadMasker, StoredEvent storedEvent, CancellationToken ct)
    {
        // ADR-027's critical invariant -- a materialization is never folded
        // and never re-materialized. Its shape was already fully validated as
        // part of the upcast success check that created it (UpcastMaterializer),
        // and it's inserted with Status "applied" directly, so in practice this
        // branch is never reached via the "received" query above -- kept as an
        // explicit, defensive check against that invariant regardless of how a
        // materialization might ever end up "received" some other way.
        if (storedEvent.EventKind == EventKind.UpcastMaterialization)
        {
            storedEvent.SchemaStatus = "conformant";
            storedEvent.Status = "applied";
            return;
        }

        // ADR-021 -- identity resolution is a per-event-TYPE decision, stable
        // across versions, computed against the ACTIVE definition -- hoisted
        // ahead of the schema-status check below, since "Compatibility &
        // Deployment Discipline"'s rollback gate needs it too.
        var activeDefinition = await schemaRegistry.GetActiveAsync(storedEvent.AppId, storedEvent.EventType, ct);
        var declaredDefinition = await schemaRegistry.GetVersionAsync(storedEvent.AppId, storedEvent.EventType, storedEvent.SchemaVersion, ct);

        // "Compatibility & Deployment Discipline" (ADR-038) -- an event
        // tagged with a schema version genuinely AHEAD of anything this
        // deployment's own registry has ever seen (declaredDefinition null
        // AND newer than the active version) is exactly what "a rolled-back
        // deployment" means: this deployment predates that shape entirely.
        // Left at Status "received" rather than advanced to "applied" --
        // ADR-023's own status envelope already keeps "durably persisted"
        // (true the moment PublishService appended it) separate from
        // "successfully routed" (this), so nothing is lost, only deferred.
        // Deliberately narrower than "declaredDefinition is null" alone: an
        // OLD/never-registered version (SchemaVersion <= active) is the
        // ordinary, already-covered "unknown schema, advisory-only" case
        // below -- SchemaStatus "unknown" but Status still reaches "applied"
        // per ADR-023's own "never gates Status" rule, unaffected by this
        // gate. The next tick's "received" query (RunOnceAsync above)
        // naturally retries this event forever, so no separate backlog-
        // reconciliation mechanism is needed -- it becomes routable the
        // moment a LATER registration raises the active version to cover
        // it, with no other code change.
        if (declaredDefinition is null && storedEvent.SchemaVersion > (activeDefinition?.Version ?? 0))
            return;

        var payloadNode = JsonNode.Parse(storedEvent.Payload) as JsonObject ?? new JsonObject();

        // ADR-023 -- schema validation against the event's OWN declared
        // SchemaVersion, resolved by (AppId, EventType, SchemaVersion) --
        // StoredEvent.AppId (added by this item) is the "dedicated fix"
        // docs/10-open-questions.md row 1 named as one way to close its own
        // ambiguity; the Router uses it directly rather than the bare-name
        // tie-break Follow/Lineage's own read-side checks still use. A
        // version that isn't registered at all is "unknown" -- nothing
        // about the payload is recognized.
        JsonObject known;
        JsonObject unknownProperties;
        ChangeKind changeKind;
        JsonNode? declaredSchemaNode = null;
        if (declaredDefinition is not null)
        {
            declaredSchemaNode = JsonNode.Parse(declaredDefinition.JsonSchema);
            var schemaNode = declaredSchemaNode;
            var errors = new List<string>();
            var conformant = JsonSchemaInstanceValidator.Validate(schemaNode, payloadNode, errors);
            storedEvent.SchemaStatus = conformant ? "conformant" : "invalid";
            (known, unknownProperties) = SplitByConformance(schemaNode, payloadNode);
            changeKind = declaredDefinition.ChangeKind;
        }
        else
        {
            storedEvent.SchemaStatus = "unknown";
            known = [];
            unknownProperties = (JsonObject)payloadNode.DeepClone();
            changeKind = ChangeKind.Partial; // safest default when nothing is known about this version's shape
        }

        // ADR-021 -- identity resolution is a per-event-TYPE decision, stable
        // across versions, so this always uses the ACTIVE version's
        // EntityIdField/EntityType, never the (possibly-unknown) declared
        // version's -- but always THIS event's own AppId, never a tie-broken
        // guess. EntityType (not storedEvent.EventType) is what makes
        // OrderPlaced and OrderShipped fold into the SAME entity -- they're
        // different event types patching one logical "Order". activeDefinition
        // itself was already resolved above, ahead of the rollback gate.
        if (activeDefinition is not null)
        {
            var uniqueId = EntityIdResolver.ResolveUniqueId(payloadNode, activeDefinition.EntityIdField);
            if (uniqueId is not null)
            {
                var entityId = $"{storedEvent.AppId}:{activeDefinition.EntityType}:{uniqueId}";
                storedEvent.EntityId = entityId;

                // ADR-042 -- the Live View folds every event immediately, no
                // AuthorityStatus gate; the authoritative Entity Store only
                // folds once AuthorityStatus reaches "accepted" (the ordinary-
                // publish default -- see PublishService). An unattested/
                // pending_review event is fully persisted and queryable in the
                // Event Log and the Live View, but doesn't yet update the
                // authoritative store.
                await FoldLiveAsync(db, entityId, storedEvent, activeDefinition.EntityType, changeKind, known, unknownProperties, ct);

                // ADR-088 -- fold-lag is recorded ONLY on this branch,
                // never for FoldLiveAsync above: an unattested/
                // pending_review event still folds into the Live View
                // immediately, but this histogram measures the
                // AUTHORITATIVE fold specifically, the one an
                // unattested/pending_review event does NOT reach here at
                // all (it waits on open-ended human review instead,
                // ADR-042) -- mixing the two would conflate mechanism
                // latency with review-workflow duration.
                if (storedEvent.AuthorityStatus == "accepted")
                {
                    using var activity = DuplexInstrumentation.ActivitySource.StartActivity("duplex.router.fold");
                    await FoldAsync(db, entityId, storedEvent, activeDefinition.EntityType, changeKind, known, unknownProperties, ct);
                    DuplexInstrumentation.RouterFoldLagMs.Record(
                        (DateTimeOffset.UtcNow - storedEvent.AppendedAt).TotalMilliseconds,
                        new KeyValuePair<string, object?>("app.id", storedEvent.AppId));
                }
            }

            // ADR-027 Trigger 1 -- a lagging publish that's already conformant
            // against its OWN declared version gets its upcast-to-active
            // result materialized immediately, using this real, just-validated
            // payload as the test case (ADR-020's own framing, still true here
            // even though the publish-time BLOCKING half of that check was
            // retired by "Entity-Centric Core Rebuild").
            if (storedEvent.SchemaStatus == "conformant" && storedEvent.SchemaVersion < activeDefinition.Version)
                await UpcastMaterializer.TryMaterializeAsync(db, schemaRegistry, upcastChain, storedEvent, activeDefinition, ct);
        }

        // "Non-Authoritative Capture" -- authorityDecision is an ordinary,
        // explicitly-registered event type (not a reserved platform type like
        // EventUpcastFailed), folding into its own entity above like any
        // other event; this is the ADDITIONAL side effect a dedicated
        // reactor performs against its TARGET event, the same "special-
        // purpose reactor" shape ADR-020/027's own handling already use.
        if (storedEvent.EventType == "authoritydecision")
            await AuthorityDecisionResolver.ProcessAsync(db, schemaRegistry, storedEvent, ct);

        // ADR-057 -- same "ordinary fold above, additional reactor effect
        // here" shape as authoritydecision just above. erasureKeyService is
        // null only for call sites (most existing tests) that never publish
        // this reserved type and have nothing to react to.
        if (storedEvent.EventType == "entityerasurerequested" && erasureKeyService is not null)
            await EntityErasureResolver.ProcessAsync(erasureKeyService, storedEvent, ct);

        // ADR-060 -- same "ordinary fold above, additional reactor effect
        // here" shape as the two reactors just above. payloadMasker is null
        // only for call sites (most existing tests) that never wire webhooks
        // and have no subscriptions to match against.
        if (payloadMasker is not null)
            await WebhookEnqueueResolver.ProcessAsync(db, payloadMasker, storedEvent, declaredSchemaNode, ct);

        storedEvent.Status = "applied";
    }

    // "Non-Authoritative Capture" -- the ungated counterpart to FoldAsync
    // below, folding into LiveEntityStoreRow instead of EntityStoreRow.
    // Deliberately simpler: no ExpectedVersion/ConflictFlag check (ADR-024's
    // Version semantics apply to the authoritative store only, per ADR-042's
    // own Consequences) and no late-arrival ordering guard (LiveEntityStoreRow
    // has no LastAppliedLogicalTime of its own -- this is the "best current
    // guess, folded in arrival order" view; late-arrival correctness is
    // specifically the authoritative view's concern, ADR-029).
    internal static async Task FoldLiveAsync(
        EventStoreContext db, string entityId, StoredEvent storedEvent, string entityType, ChangeKind changeKind,
        JsonObject known, JsonObject unknownProperties, CancellationToken ct)
    {
        // Checks already-tracked-but-not-yet-saved rows first (RunOnceAsync
        // saves once per tick, after processing every "received" event in a
        // batch loop) -- two events for the SAME entity landing in one tick is
        // an ordinary, expected case (a burst of activity, or catching up
        // after any delay), not an edge case. A plain SingleOrDefaultAsync
        // query here would never see the first event's own not-yet-saved
        // Add()ed row (a LINQ query only sees committed rows), so the second
        // event would Add() a SECOND row with the same key and crash with an
        // identity-conflict exception at SaveChangesAsync time -- found by
        // actually running a multi-event-per-entity-per-tick scenario (this
        // item's own restore-drill test), not by reading the code back.
        var row = db.LiveEntityStore.Local.FirstOrDefault(r => r.EntityId == entityId)
            ?? await db.LiveEntityStore.SingleOrDefaultAsync(r => r.EntityId == entityId, ct);
        if (row is null)
        {
            row = new Domain.EntityStore.LiveEntityStoreRow { EntityId = entityId, EntityType = entityType, Data = "{}", Extensions = "{}" };
            db.LiveEntityStore.Add(row);
        }

        var mergedData = changeKind == ChangeKind.Full
            ? (JsonObject)known.DeepClone()
            : EntityDataMerger.MergePatch(JsonNode.Parse(row.Data), known);
        var mergedExtensions = EntityDataMerger.MergePatch(JsonNode.Parse(row.Extensions), unknownProperties);

        row.Data = mergedData.ToJsonString();
        row.Extensions = mergedExtensions.ToJsonString();
        row.AuthorityStatus = storedEvent.AuthorityStatus; // the MOST RECENT contributing event's status -- never rolled up/hidden (ADR-042)
        row.LastAppliedSequenceNumber = storedEvent.SequenceNumber;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Splits a payload's own top-level properties into (a) declared in the
    // schema AND individually valid -- folded normally -- and (b) not
    // declared at all -- routed to Extensions (ADR-022). A property that IS
    // declared but fails its own validation (e.g. Amount: "not-a-number"
    // against a number schema) is neither: ADR-023 folds known-good data,
    // never a value that's individually invalid for its own known slot.
    internal static (JsonObject Known, JsonObject Unknown) SplitByConformance(JsonNode? schemaNode, JsonObject payload)
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

    internal static async Task FoldAsync(
        EventStoreContext db, string entityId, StoredEvent storedEvent, string entityType, ChangeKind changeKind,
        JsonObject known, JsonObject unknownProperties, CancellationToken ct)
    {
        // Same not-yet-saved-local-row check as FoldLiveAsync above, and for
        // the identical reason -- two events for one entity in the same tick.
        var row = db.EntityStore.Local.FirstOrDefault(r => r.EntityId == entityId)
            ?? await db.EntityStore.SingleOrDefaultAsync(r => r.EntityId == entityId, ct);
        if (row is null)
        {
            row = new EntityStoreRow
            {
                EntityId = entityId,
                EntityType = entityType,
                ShardKey = entityType, // ADR-034 -- ShardKey = EntityType, the default and only v1 mechanism
                Version = 0,
                Data = "{}",
                Extensions = "{}",
                LastAppliedLogicalTime = DateTimeOffset.MinValue,
            };
            db.EntityStore.Add(row);
        }

        // ADR-024 -- ConflictFlag: a stale ExpectedVersion never blocks the
        // write, it only flags the later-applied event.
        if (storedEvent.ExpectedVersion is { } expected && expected != row.Version)
            storedEvent.ConflictFlag = true;

        // ADR-029 -- LateArrivalFlag: unlike ConflictFlag, this DOES gate
        // whether the change is applied -- applying a chronologically-stale
        // value would silently revert already-folded newer state.
        var isLateArrival = storedEvent.OccurredAt <= row.LastAppliedLogicalTime;
        storedEvent.LateArrivalFlag = isLateArrival;

        row.LastAppliedSequenceNumber = storedEvent.SequenceNumber; // always advances, even past a rejected late arrival

        if (!isLateArrival)
        {
            var mergedData = changeKind == ChangeKind.Full
                ? (JsonObject)known.DeepClone()
                : EntityDataMerger.MergePatch(JsonNode.Parse(row.Data), known);
            var mergedExtensions = EntityDataMerger.MergePatch(JsonNode.Parse(row.Extensions), unknownProperties);

            var newDataJson = mergedData.ToJsonString();
            if (newDataJson != row.Data)
                row.Version += 1; // ADR-021/029 -- only bumps when Data actually changes
            row.Data = newDataJson;
            row.Extensions = mergedExtensions.ToJsonString();
            row.SchemaVersion = storedEvent.SchemaVersion;
            row.Hash = ComputeHash(newDataJson);
            row.LastAppliedLogicalTime = storedEvent.OccurredAt;
            row.LastAppliedOriginId = storedEvent.OriginId; // ADR-033 -- which site's event most recently won this fold
        }

        row.LateArrivalFlag = isLateArrival;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ComputeHash(string dataJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataJson))).ToLowerInvariant();

    // "Non-Authoritative Capture", comparisons/authority-rejection-behavior.md's
    // targeted-rebuild refinement -- adopted as RejectionBehavior.Annotate's
    // real default (2026-08-12), replacing the prior "leave the Entity Store
    // exactly as it was" behavior. Called by AuthorityDecisionResolver only
    // when an already-accepted-and-folded event is reversed to "rejected"
    // (ADR-042's own narrowing -- an event never accepted has nothing to
    // rebuild away). Re-folds this ONE entity's entire event history from a
    // blank slate, including only events whose CURRENT AuthorityStatus is
    // "accepted" -- re-evaluated fresh here, not the status each event held
    // at its own original processing time, since that's exactly what may
    // have just changed. The Event Log itself is never touched (AsNoTracking,
    // and ConflictFlag/LateArrivalFlag on each source StoredEvent are
    // deliberately never reassigned here -- they remain the permanent record
    // of what happened at that event's own original processing time, not
    // overwritten by a later rebuild's own internal late-arrival bookkeeping,
    // which is why isLateArrival below is a local, not a field write). Only
    // the DERIVED Entity Store cache is recomputed -- consistent with
    // README.md's "never lose or corrupt data": the immutable history is
    // what makes this replay possible and correct in the first place.
    internal static async Task RebuildEntityFromAcceptedEventsAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, string entityId, CancellationToken ct)
    {
        var events = await db.Events.AsNoTracking()
            .Where(e => e.EntityId == entityId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);

        var row = await db.EntityStore.SingleAsync(r => r.EntityId == entityId, ct);
        row.Data = "{}";
        row.Extensions = "{}";
        row.Version = 0;
        row.LastAppliedLogicalTime = DateTimeOffset.MinValue;

        var anyAccepted = false;
        foreach (var storedEvent in events.Where(e => e.AuthorityStatus == "accepted"))
        {
            anyAccepted = true;
            var definition = await schemaRegistry.GetVersionAsync(storedEvent.AppId, storedEvent.EventType, storedEvent.SchemaVersion, ct);
            if (definition is null)
                continue; // shouldn't happen -- this event already resolved an EntityId once, under this same version

            var payloadNode = JsonNode.Parse(storedEvent.Payload) as JsonObject ?? new JsonObject();
            var (known, unknown) = SplitByConformance(JsonNode.Parse(definition.JsonSchema), payloadNode);

            // Same late-arrival guard FoldAsync uses (ADR-029) -- re-evaluated
            // against THIS replay's own accumulating LastAppliedLogicalTime,
            // never written back to the source StoredEvent. row.LateArrivalFlag
            // (unlike the per-event flag) IS a rolled-up field on the row
            // itself (ADR-029's own comment: "rolled up from contributing
            // events") -- FoldAsync always reassigns it per fold, so this
            // rebuild does too, for the same "reflects the most recently
            // processed contribution" semantics.
            var isLateArrival = storedEvent.OccurredAt <= row.LastAppliedLogicalTime;
            row.LateArrivalFlag = isLateArrival;
            row.LastAppliedSequenceNumber = storedEvent.SequenceNumber;
            if (isLateArrival)
                continue;

            var mergedData = definition.ChangeKind == ChangeKind.Full
                ? (JsonObject)known.DeepClone()
                : EntityDataMerger.MergePatch(JsonNode.Parse(row.Data), known);
            var mergedExtensions = EntityDataMerger.MergePatch(JsonNode.Parse(row.Extensions), unknown);

            var newDataJson = mergedData.ToJsonString();
            if (newDataJson != row.Data)
                row.Version += 1;
            row.Data = newDataJson;
            row.Extensions = mergedExtensions.ToJsonString();
            row.SchemaVersion = storedEvent.SchemaVersion;
            row.Hash = ComputeHash(newDataJson);
            row.LastAppliedLogicalTime = storedEvent.OccurredAt;
            row.LastAppliedOriginId = storedEvent.OriginId;
        }

        // Zero surviving contributions -- the entity never existed from the
        // remaining, trustworthy history's point of view. Removing the row
        // outright (rather than leaving a hollow "{}" shell) matches ADR-042's
        // own gate: an entity with no accepted events gets NO authoritative
        // row at all, the same rule that applies before any event is ever
        // accepted for the first time.
        if (!anyAccepted)
            db.EntityStore.Remove(row);
        else
            row.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
