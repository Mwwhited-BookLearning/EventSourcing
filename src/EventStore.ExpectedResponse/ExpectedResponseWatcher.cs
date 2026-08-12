using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.SchemaRegistry;
using EventStore.Inbox;
using EventStore.LeaderElection;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.WorkerWakeSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.ExpectedResponse;

// ADR-094 -- maintains ExpectedResponseTracker rows and escalates an unmet
// deadline to the reserved ExpectedResponseMissing event. Architecturally
// an "internal follower" per the ADR's own text (the same shape
// ProjectionHost uses), but -- like Router/Derivation/Webhooks before it --
// actually built as a same-process worker reading EventStoreContext
// directly, not a separate Follow-over-HTTP client: this mechanism lives in
// the same database/process as the events it tails, so there is no real
// process boundary for a Follow client to cross. Leader-lease-gated
// exactly like those three (ADR-078), same BackgroundService + testable
// static RunOnceAsync shape.
public class ExpectedResponseWatcher(IServiceScopeFactory scopeFactory, ILogger<ExpectedResponseWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // ADR-095 -- shared with PublishService's own NotifyAsync call via
    // WakeSignalTopics (see WakeSignalTopics.cs's own comment for why).
    public const string Topic = WakeSignalTopics.ExpectedResponse;

    private const string WorkerRole = "ExpectedResponseWatcher";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(2.5);

    // ExpectedResponseMissing declares no RequiredClaims (mirroring
    // ChannelLagDetectedEventType), so an empty principal is never
    // Forbidden publishing it -- same reasoning TelemetrySampleWriter's/
    // DerivationWorker's own SystemPrincipal already establish.
    private static readonly ClaimsPrincipal SystemPrincipal = new(new ClaimsIdentity());

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
                        logger.LogInformation("Expected-response watcher {State} the {WorkerRole} lease", isLeader ? "acquired" : "lost", WorkerRole);
                    }
                    nextRenewalAt = isLeader ? DateTimeOffset.UtcNow + RenewInterval : DateTimeOffset.MinValue;
                }

                if (isLeader)
                {
                    var schemaRegistry = scope.ServiceProvider.GetRequiredService<SchemaRegistryService>();
                    var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();
                    await RunOnceAsync(db, schemaRegistry, publishService, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A tick failing (a transient DB error) must not take the whole
                // worker down -- it retries next tick, same resiliency posture
                // as every other worker in this solution.
                logger.LogError(ex, "Expected-response watcher tick failed");
            }

            // ADR-095 -- same shape RouterWorker established first.
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
    // pattern RouterWorker/DerivationWorker/WebhookOutboxPump already
    // established. Three duties, run every tick: (1) open a tracker row for
    // every new request event, (2) satisfy any tracker whose response has
    // now arrived, (3) sweep and escalate any tracker past its own deadline.
    public static async Task RunOnceAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publishService, CancellationToken ct = default)
    {
        // Client-side filter for ExpectedResponse specifically -- it's a
        // JSON-converted, class-typed property; keeping the DB-side predicate
        // to the plain IsActive column is the same two-step
        // query-then-filter posture DerivationWorker's own
        // "Where(d => d.IsActive).ToListAsync()" already established.
        var activeDefinitions = await db.EventTypeDefinitions.Where(d => d.IsActive).ToListAsync(ct);
        var tracked = activeDefinitions.Where(d => d.ExpectedResponse is not null).ToList();

        foreach (var definition in tracked)
            await OpenTrackersForNewRequestsAsync(db, definition, ct);

        foreach (var responseEventType in tracked.Select(d => d.ExpectedResponse!.ResponseEventType).Distinct())
            await SatisfyTrackersAsync(db, responseEventType, ct);

        await SweepAndEscalateAsync(db, schemaRegistry, publishService, ct);
    }

    private static async Task OpenTrackersForNewRequestsAsync(EventStoreContext db, Domain.SchemaRegistry.EventTypeDefinition definition, CancellationToken ct)
    {
        var expectedResponse = definition.ExpectedResponse!;

        // A left-anti-join against ExpectedResponseTrackers, not a separate
        // cursor table -- the tracker's own PK (RequestEventId) is already
        // the "have I opened one of these yet" marker this needs, so a
        // second, parallel cursor would track nothing a NOT EXISTS query
        // can't already answer directly.
        var newRequests = await db.Events
            .Where(e => e.AppId == definition.AppId && e.EventType == definition.Name)
            .Where(e => !db.ExpectedResponseTrackers.Any(t => t.RequestEventId == e.EventId))
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);

        foreach (var requestEvent in newRequests)
        {
            db.ExpectedResponseTrackers.Add(new ExpectedResponseTracker
            {
                RequestEventId = requestEvent.EventId,
                RequestEventType = requestEvent.EventType,
                ExpectedResponseEventType = expectedResponse.ResponseEventType,
                DeadlineAt = requestEvent.AppendedAt + expectedResponse.Within, // "this event's receipt time + Within" (ADR-094) -- AppendedAt is server receipt time, ADR-088
            });
        }

        if (newRequests.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static async Task SatisfyTrackersAsync(EventStoreContext db, string responseEventType, CancellationToken ct)
    {
        // Every response-type event carrying a RespondsToEventId, every tick --
        // unindexed by design at this build stage, the same posture
        // DerivationWorker's own FindLatestMatchingEventAsync already
        // established for an identical full-history scan.
        var candidateResponses = await db.Events
            .Where(e => e.EventType == responseEventType && e.RespondsToEventId != null)
            .ToListAsync(ct);

        var changed = false;
        foreach (var response in candidateResponses)
        {
            var tracker = await db.ExpectedResponseTrackers.SingleOrDefaultAsync(t => t.RequestEventId == response.RespondsToEventId, ct);
            // A response naming a RespondsToEventId with no open tracker (no
            // ExpectedResponse ever configured for that request type, or the
            // request event doesn't resolve) is simply not tracked -- the
            // same "correlates to nothing findable" posture ADR-094 already
            // establishes for RespondsToEventId itself. An already-satisfied
            // tracker is left alone -- first response wins; a later duplicate
            // reply is still fully persisted in the Event Log, just not
            // re-recorded on the tracker row (this design's "never lose
            // data" principle is satisfied by the Event Log itself, not by
            // this derived bookkeeping row).
            if (tracker is null || tracker.SatisfiedByEventId is not null)
                continue;

            tracker.SatisfiedByEventId = response.EventId;
            tracker.SatisfiedAt = response.AppendedAt; // on time or late -- never treated as an error (ADR-094)
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    private static async Task SweepAndEscalateAsync(EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publishService, CancellationToken ct)
    {
        // SQLite's own EF Core provider translates only equality on a
        // DateTimeOffset column, not "<=" -- the same client-side-filter
        // workaround DerivationWorker's own SweepExpiredPendingJoinsAsync
        // already established for PendingJoinState.ExpiresAt.
        var unresolved = await db.ExpectedResponseTrackers
            .Where(t => t.SatisfiedAt == null && t.EscalatedAt == null)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var overdue = unresolved.Where(t => t.DeadlineAt <= now).ToList();
        if (overdue.Count == 0)
            return;

        foreach (var tracker in overdue)
            await EscalateAsync(db, schemaRegistry, publishService, tracker, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EscalateAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publishService, ExpectedResponseTracker tracker, CancellationToken ct)
    {
        // ExpectedResponseTracker's own shape is exactly ADR-094's literal 7
        // fields -- no denormalized AppId of its own -- so the request
        // event's AppId is looked up the same way EntityErasureResolver/
        // AuthorityDecisionResolver already look up their own target event.
        var requestEvent = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == tracker.RequestEventId, ct);
        if (requestEvent is null)
            return; // shouldn't happen -- the request event is what created this tracker row in the first place

        await ExpectedResponseMissingEventType.EnsureRegisteredAsync(schemaRegistry, requestEvent.AppId, ct);

        var payload = JsonSerializer.Serialize(new
        {
            tracker.RequestEventId,
            tracker.RequestEventType,
            tracker.ExpectedResponseEventType,
            tracker.DeadlineAt,
        });

        // RespondsToEventId is set back at the original request (ADR-094) --
        // "everything that references event X" (its children, its response,
        // and now its missing-response escalation) all pivot through this
        // one generic field, never a second mechanism.
        await publishService.PublishAsync(ExpectedResponseMissingEventType.Name,
            new PublishEventRequest(requestEvent.AppId, 1, payload, null, null, RespondsToEventId: tracker.RequestEventId),
            SystemPrincipal, ct);

        tracker.EscalatedAt = DateTimeOffset.UtcNow; // fires exactly once, even if a later sweep runs again before a late response arrives
    }
}
