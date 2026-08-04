using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EventStore.Domain.EntityStore;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();
                var schemaRegistry = scope.ServiceProvider.GetRequiredService<SchemaRegistryService>();
                var upcastChain = scope.ServiceProvider.GetRequiredService<UpcastChain>();
                await RunOnceAsync(db, schemaRegistry, upcastChain, stoppingToken);
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

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    // One tick's worth of work, factored out of ExecuteAsync's loop so tests
    // can drive it directly against a provider-backed context, the same
    // pattern DerivationWorker.RunOnceAsync already established. Returns the
    // number of events processed this tick (received events only -- ADR-027's
    // Trigger 2 backlog reconciliation runs every tick too, but isn't counted
    // here since it's not driven by "received" events at all).
    public static async Task<int> RunOnceAsync(EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain, CancellationToken ct = default)
    {
        var received = await db.Events
            .Where(e => e.Status == "received")
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);

        foreach (var storedEvent in received)
            await ProcessEventAsync(db, schemaRegistry, upcastChain, storedEvent, ct);

        if (received.Count > 0)
            await db.SaveChangesAsync(ct);

        // ADR-027 Trigger 2 -- catches up any backlog left by a mapping that
        // didn't exist yet when its events originally folded.
        await UpcastMaterializer.ReconcileBacklogAsync(db, schemaRegistry, upcastChain, ct);

        return received.Count;
    }

    private static async Task ProcessEventAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, UpcastChain upcastChain, StoredEvent storedEvent, CancellationToken ct)
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
        var declaredDefinition = await schemaRegistry.GetVersionAsync(storedEvent.AppId, storedEvent.EventType, storedEvent.SchemaVersion, ct);
        if (declaredDefinition is not null)
        {
            var schemaNode = JsonNode.Parse(declaredDefinition.JsonSchema);
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
        // different event types patching one logical "Order".
        var activeDefinition = await schemaRegistry.GetActiveAsync(storedEvent.AppId, storedEvent.EventType, ct);
        if (activeDefinition is not null)
        {
            var uniqueId = EntityIdResolver.ResolveUniqueId(payloadNode, activeDefinition.EntityIdField);
            if (uniqueId is not null)
            {
                var entityId = $"{storedEvent.AppId}:{activeDefinition.EntityType}:{uniqueId}";
                storedEvent.EntityId = entityId;
                await FoldAsync(db, entityId, storedEvent, activeDefinition.EntityType, changeKind, known, unknownProperties, ct);
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

        storedEvent.Status = "applied";
    }

    // Splits a payload's own top-level properties into (a) declared in the
    // schema AND individually valid -- folded normally -- and (b) not
    // declared at all -- routed to Extensions (ADR-022). A property that IS
    // declared but fails its own validation (e.g. Amount: "not-a-number"
    // against a number schema) is neither: ADR-023 folds known-good data,
    // never a value that's individually invalid for its own known slot.
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

    private static async Task FoldAsync(
        EventStoreContext db, string entityId, StoredEvent storedEvent, string entityType, ChangeKind changeKind,
        JsonObject known, JsonObject unknownProperties, CancellationToken ct)
    {
        var row = await db.EntityStore.SingleOrDefaultAsync(r => r.EntityId == entityId, ct);
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
}
