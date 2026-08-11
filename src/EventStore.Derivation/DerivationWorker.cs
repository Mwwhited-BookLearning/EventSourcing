using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Derivation;

// ADR-007's "internal follower": one polling loop drives every active
// DerivationDefinition, same tail-and-poll shape EventTailReader uses for
// the Follow API, then republishes through the ordinary publish path
// (PublishService), which is what actually records parentEventIds via
// EventParents -- no separate provenance mechanism needed.
public class DerivationWorker(IServiceScopeFactory scopeFactory, ILogger<DerivationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int SourceBatchSize = 200;

    // The worker is a server-side process producing new data, not exposing
    // existing data to an external caller, so RequiredClaims' Read direction
    // never applies to it (docs/data/schema-registry.md). Publish-direction
    // claims are always empty for a derived type -- DerivationRegistrationService
    // always registers one with RequiredClaims: null -- so an empty principal
    // always satisfies PublishService's own claim gate for it.
    private static readonly ClaimsPrincipal SystemPrincipal = new(new ClaimsIdentity());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();
                var schemaRegistry = scope.ServiceProvider.GetRequiredService<SchemaRegistryService>();
                var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();

                await RunOnceAsync(db, schemaRegistry, publishService, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A derivation tick failing (a transient DB error, a malformed payload
                // from a source that was never actually validated against this
                // derivation's own expectations) must not take the whole worker down --
                // it retries next tick, same resiliency posture as EventTailReader's own
                // poll loop.
                logger.LogError(ex, "Derivation worker tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    // One tick's worth of work, factored out of ExecuteAsync's loop so tests can
    // drive it directly against a provider-backed context (constructing the whole
    // hosted-service scaffolding, same as every other build-plan item's tests
    // exercise the underlying service directly rather than the ASP.NET host).
    public static async Task RunOnceAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publishService, CancellationToken ct = default)
    {
        var activeDerivations = await db.DerivationDefinitions.Where(d => d.IsActive).ToListAsync(ct);
        foreach (var derivation in activeDerivations)
            await ProcessDerivationAsync(db, schemaRegistry, publishService, derivation, ct);
        await SweepExpiredPendingJoinsAsync(db, ct);
    }

    private static async Task ProcessDerivationAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publishService,
        DerivationDefinition derivation, CancellationToken ct)
    {
        var sources = derivation.Sources.Distinct().ToList();
        var cursors = await db.DerivationCursors
            .Where(c => c.AppId == derivation.AppId && c.DerivationName == derivation.Name)
            .ToDictionaryAsync(c => c.SourceEventType, ct);

        var newEvents = new List<StoredEvent>();
        foreach (var source in sources)
        {
            if (!cursors.TryGetValue(source, out var cursor))
                continue; // no cursor row -- source was never registered for this derivation (shouldn't happen)

            var batch = await db.Events
                .AsNoTracking()
                .Where(e => e.EventType == source && e.SequenceNumber > cursor.LastProcessedSequenceNumber)
                .OrderBy(e => e.SequenceNumber)
                .Take(SourceBatchSize)
                .ToListAsync(ct);
            newEvents.AddRange(batch);
        }

        if (newEvents.Count == 0)
            return;

        // Global arrival order across every declared source, not per-source order --
        // matters for FireOnce's "first-seen" semantics when two sources' events land
        // in the same tick.
        newEvents = newEvents.OrderBy(e => e.SequenceNumber).ToList();

        var activeDefinition = await schemaRegistry.GetActiveAsync(derivation.AppId, derivation.Name, ct);
        if (activeDefinition is null)
            return; // the derived type's own EventTypeDefinition was deactivated/removed out-of-band; nothing to publish into

        var (classOf, classCount) = BuildJoinKeyClasses(derivation.JoinConditions);

        foreach (var storedEvent in newEvents)
        {
            var payloadNode = JsonNode.Parse(storedEvent.Payload);
            var joinKey = ComputeJoinKey(storedEvent.EventType, payloadNode, classOf, classCount);

            if (derivation.JoinTriggerMode == JoinTriggerMode.FireOnce)
                await HandleFireOnceArrivalAsync(db, publishService, derivation, activeDefinition, storedEvent, joinKey, ct);
            else
                await HandleContinuousArrivalAsync(db, publishService, derivation, activeDefinition, storedEvent, joinKey, sources, ct);

            if (cursors.TryGetValue(storedEvent.EventType, out var cursor))
                cursor.LastProcessedSequenceNumber = Math.Max(cursor.LastProcessedSequenceNumber, storedEvent.SequenceNumber);

            // Saved per event, not once after the whole batch: a PendingJoinState
            // row Add()ed for one event in this batch must already be persisted
            // before the NEXT event in the same batch queries for it via
            // SingleOrDefaultAsync -- Add() alone doesn't make a new row visible to
            // a subsequent LINQ query against the same DbContext.
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task HandleFireOnceArrivalAsync(
        EventStoreContext db, PublishService publishService, DerivationDefinition derivation,
        EventTypeDefinition activeDefinition, StoredEvent arrivingEvent, string joinKey, CancellationToken ct)
    {
        var pending = await db.PendingJoinStates.SingleOrDefaultAsync(p =>
            p.AppId == derivation.AppId && p.DerivationName == derivation.Name &&
            p.JoinKeyValue == joinKey && p.ExpiredReason == null, ct);

        var arrived = pending is null
            ? new Dictionary<string, ArrivedSource>()
            : JsonSerializer.Deserialize<Dictionary<string, ArrivedSource>>(pending.ArrivedSourcesJson)!;
        arrived[arrivingEvent.EventType] = new ArrivedSource(arrivingEvent.EventId, arrivingEvent.Payload, arrivingEvent.DerivationHopCount);

        var allSourcesArrived = derivation.Sources.Distinct().All(arrived.ContainsKey);
        if (!allSourcesArrived)
        {
            if (pending is null)
            {
                var now = DateTimeOffset.UtcNow;
                db.PendingJoinStates.Add(new PendingJoinState
                {
                    Id = Guid.NewGuid(),
                    AppId = derivation.AppId,
                    DerivationName = derivation.Name,
                    JoinKeyValue = joinKey,
                    ArrivedSourcesJson = JsonSerializer.Serialize(arrived),
                    FirstSeenAt = now,
                    ExpiresAt = now + derivation.PendingJoinTtl,
                });
            }
            else
            {
                pending.ArrivedSourcesJson = JsonSerializer.Serialize(arrived);
            }
            return;
        }

        var hopCount = 1 + arrived.Values.Max(a => a.HopCount);
        if (hopCount > derivation.MaxHopCount)
        {
            // Belt-and-suspenders cap (ADR-007) -- the row is kept, not deleted, as a
            // minimal dead-letter record an operator can inspect; the registration-time
            // cycle check is what actually prevents this in the ordinary case.
            if (pending is null)
            {
                var now = DateTimeOffset.UtcNow;
                db.PendingJoinStates.Add(new PendingJoinState
                {
                    Id = Guid.NewGuid(),
                    AppId = derivation.AppId,
                    DerivationName = derivation.Name,
                    JoinKeyValue = joinKey,
                    ArrivedSourcesJson = JsonSerializer.Serialize(arrived),
                    FirstSeenAt = now,
                    ExpiresAt = now + derivation.PendingJoinTtl,
                    ExpiredReason = "hop_limit_exceeded",
                });
            }
            else
            {
                pending.ArrivedSourcesJson = JsonSerializer.Serialize(arrived);
                pending.ExpiredReason = "hop_limit_exceeded";
            }
            return;
        }

        await PublishDerivedEventAsync(publishService, derivation, activeDefinition, arrived, hopCount, ct);

        if (pending is not null)
            db.PendingJoinStates.Remove(pending);
    }

    // Simplification, documented rather than left implicit: the first join for a
    // given key still requires every declared source to have arrived at least once
    // (same as FireOnce's own completion condition) -- only once that baseline
    // exists does a later arrival on any one source alone trigger a re-emission
    // against the current latest state of the others (ADR-007's "current latest
    // state" is looked up directly against StoredEvent, not a rebuilt cache, so a
    // worker restart needs no separate warm-up step).
    private static async Task HandleContinuousArrivalAsync(
        EventStoreContext db, PublishService publishService, DerivationDefinition derivation,
        EventTypeDefinition activeDefinition, StoredEvent arrivingEvent, string joinKey,
        IReadOnlyList<string> sources, CancellationToken ct)
    {
        var arrived = new Dictionary<string, ArrivedSource>
        {
            [arrivingEvent.EventType] = new(arrivingEvent.EventId, arrivingEvent.Payload, arrivingEvent.DerivationHopCount),
        };

        foreach (var otherSource in sources.Where(s => s != arrivingEvent.EventType))
        {
            var match = await FindLatestMatchingEventAsync(db, otherSource, joinKey, derivation.JoinConditions, ct);
            if (match is null)
                return; // this join key hasn't been seen on every source yet -- no enrichment possible
            arrived[otherSource] = new ArrivedSource(match.EventId, match.Payload, match.DerivationHopCount);
        }

        var hopCount = 1 + arrived.Values.Max(a => a.HopCount);
        if (hopCount > derivation.MaxHopCount)
        {
            db.PendingJoinStates.Add(new PendingJoinState
            {
                Id = Guid.NewGuid(),
                AppId = derivation.AppId,
                DerivationName = derivation.Name,
                JoinKeyValue = joinKey,
                ArrivedSourcesJson = JsonSerializer.Serialize(arrived),
                FirstSeenAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow,
                ExpiredReason = "hop_limit_exceeded",
            });
            return;
        }

        await PublishDerivedEventAsync(publishService, derivation, activeDefinition, arrived, hopCount, ct);
    }

    private static async Task<StoredEvent?> FindLatestMatchingEventAsync(
        EventStoreContext db, string otherSource, string joinKey, List<JoinCondition> joinConditions, CancellationToken ct)
    {
        var (classOf, classCount) = BuildJoinKeyClasses(joinConditions);
        var candidates = await db.Events
            .AsNoTracking()
            .Where(e => e.EventType == otherSource)
            .OrderByDescending(e => e.SequenceNumber)
            .ToListAsync(ct); // unindexed by design at this build stage -- see DerivationWorker's own note in docs/changes

        foreach (var candidate in candidates)
        {
            var candidateKey = ComputeJoinKey(candidate.EventType, JsonNode.Parse(candidate.Payload), classOf, classCount);
            if (candidateKey == joinKey)
                return candidate;
        }

        return null;
    }

    private static async Task PublishDerivedEventAsync(
        PublishService publishService, DerivationDefinition derivation, EventTypeDefinition activeDefinition,
        Dictionary<string, ArrivedSource> arrived, int hopCount, CancellationToken ct)
    {
        var outputPayload = BuildOutputPayload(derivation.SelectFields, arrived);
        var parentEventIds = arrived.Values.Select(a => a.EventId).ToList();

        await publishService.PublishAsync(
            derivation.Name,
            new PublishEventRequest(derivation.AppId, activeDefinition.Version, outputPayload, parentEventIds, EventId: null),
            SystemPrincipal,
            ct,
            derivationHopCount: hopCount);
    }

    private static string BuildOutputPayload(List<SelectField> selectFields, IReadOnlyDictionary<string, ArrivedSource> arrived)
    {
        var output = new JsonObject();
        foreach (var field in selectFields)
        {
            var sourcePayload = JsonNode.Parse(arrived[field.SourceType].Payload);
            var value = sourcePayload is JsonObject obj && obj.TryGetPropertyValue(field.SourceField, out var v) ? v : null;
            output[field.OutputField] = value?.DeepClone();
        }
        return output.ToJsonString();
    }

    private static async Task SweepExpiredPendingJoinsAsync(EventStoreContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // The ExpiresAt <= now comparison is done client-side, not pushed into the
        // Where clause -- SQLite's EF Core provider does not support translating
        // relational operators (other than equality) on DateTimeOffset columns.
        // Not-yet-expired candidates are already a small set at this build scale.
        var candidates = await db.PendingJoinStates
            .Where(p => p.ExpiredReason == null)
            .ToListAsync(ct);
        var expired = candidates.Where(p => p.ExpiresAt <= now).ToList();
        foreach (var p in expired)
            p.ExpiredReason = "ttl_expired";
        if (expired.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // ADR-007's $on is a conjunction of pairwise field equalities across
    // Sources -- a union-find over (source, field) endpoints groups every
    // field transitively tied to the same logical join key into one class,
    // so an n-ary join (not just a single shared column) is supported
    // without special-casing the 2-source case.
    private static (Dictionary<(string Source, string Field), int> ClassOf, int ClassCount) BuildJoinKeyClasses(
        List<JoinCondition> joinConditions)
    {
        var parent = new Dictionary<(string, string), (string, string)>();

        (string, string) Find((string, string) x)
        {
            if (!parent.TryGetValue(x, out var p))
            {
                parent[x] = x;
                return x;
            }
            if (p == x)
                return x;
            var root = Find(p);
            parent[x] = root;
            return root;
        }

        foreach (var jc in joinConditions)
        {
            var left = (jc.LeftSource, jc.LeftField);
            var right = (jc.RightSource, jc.RightField);
            var rootLeft = Find(left);
            var rootRight = Find(right);
            if (rootLeft != rootRight)
                parent[rootLeft] = rootRight;
        }

        var classOf = new Dictionary<(string, string), int>();
        var nextId = 0;
        foreach (var key in parent.Keys.ToList())
        {
            var root = Find(key);
            if (!classOf.TryGetValue(root, out var id))
            {
                id = nextId++;
                classOf[root] = id;
            }
            classOf[key] = id;
        }

        return (classOf, nextId);
    }

    private static string ComputeJoinKey(
        string source, JsonNode? payloadNode, Dictionary<(string Source, string Field), int> classOf, int classCount)
    {
        var parts = new string?[classCount];
        foreach (var ((s, f), classId) in classOf)
        {
            if (s != source)
                continue;
            parts[classId] = payloadNode is JsonObject obj && obj.TryGetPropertyValue(f, out var v) ? v?.ToJsonString() : null;
        }
        return string.Join('\u0001', parts.Select(p => p ?? ""));
    }

    private record ArrivedSource(Guid EventId, string Payload, int HopCount);
}
