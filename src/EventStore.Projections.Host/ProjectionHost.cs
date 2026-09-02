using System.Diagnostics;
using System.Text.Json.Nodes;
using EventStore.Projections.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Projections.Host;

// docs/09-cqrs-read-models.md's "generic part, written once" -- one instance
// per registered IProjection<T>. batchSize is always 1 (the checkpoint
// advances after every event) -- the design's own configurable-batchSize
// throughput trade-off (advancing less often) is not built at this stage,
// noted explicitly rather than silently dropped.
public class ProjectionHost<TReadModel>(
    IServiceScopeFactory scopeFactory,
    IProjection<TReadModel> projection,
    FollowClient followClient,
    IOptions<ProjectionHostOptions> options,
    ILogger<ProjectionHost<TReadModel>> logger)
    : BackgroundService
    where TReadModel : class
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    // Serializes every apply (snapshot + read-model upsert + checkpoint
    // advance) across every concurrently-tailed event type in this
    // projection -- ProjectionCheckpoint has one row per ProjectionName, not
    // per event type (docs/09-cqrs-read-models.md), so two event types'
    // concurrent tail loops writing that same row without this would race.
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await Task.WhenAll(projection.EventTypes.Select(eventType => TailForeverAsync(eventType, stoppingToken)));

    private async Task TailForeverAsync(string eventType, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // UnregisteredEventType/Forbidden are routine, expected
                // outcomes (CatchUpOnceAsync's own comment) -- logged there,
                // not here; only a genuinely unexpected exception reaches
                // this catch block now.
                await CatchUpOnceAsync(eventType, maxEventsToConsume: int.MaxValue, idleTimeout: Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Projection {Projection} lost its {EventType} connection; reconnecting", projection.Name, eventType);
            }

            await Task.Delay(ReconnectDelay, ct);
        }
    }

    // Consumes up to maxEventsToConsume events from eventType's Follow tail
    // (or indefinitely, with Timeout.InfiniteTimeSpan), applying each via
    // SnapshotMerger and upserting the projected read-model row. Stops early
    // once idleTimeout elapses with no new event -- lets a caller (a test, or
    // ExecuteAsync's own reconnect loop) drive one bounded catch-up pass
    // deterministically rather than an unboundedly live stream, the same
    // "exercise the mechanics directly, with a timeout" pattern this repo's
    // own Follow/Masking tests already use for Follow's inherently-infinite
    // SSE stream.
    // Returns the outcome alongside the count rather than just the count --
    // UnregisteredEventType/Forbidden are routine, expected states (a
    // reserved/late-registered event type with no schema yet for this AppId,
    // or a RequiredClaims gate this projection's own token doesn't satisfy),
    // never thrown as exceptions (docs/patterns/known-outcomes-are-not-
    // exceptions.md) -- logged at Warning here, once, rather than left for
    // TailForeverAsync's own catch block to misclassify at Error forever, the
    // exact bug docs/bugs/framework/service/rbac-fold-404-logged-as-error-
    // forever.md found for RbacProjectionWorker's own identical shape.
    public async Task<(CatchUpOutcome Outcome, int EventsConsumed)> CatchUpOnceAsync(string eventType, int maxEventsToConsume, TimeSpan idleTimeout, CancellationToken ct)
    {
        // ADR-101: a projection can force its own ChangeKind for this event
        // type (EventStore.Flows.FlowProjection does, for resolver event
        // types) without touching that type's real global registration --
        // short-circuits the lookup entirely when overridden, computed once
        // per catch-up cycle here, same as the un-overridden case always was.
        ChangeKind changeKind;
        if (projection.OverrideChangeKind(eventType) is { } overridden)
        {
            changeKind = overridden;
        }
        else
        {
            switch (await followClient.GetChangeKindAsync(eventType, ct))
            {
                case ChangeKindResult.Found(var found):
                    changeKind = found;
                    break;
                case ChangeKindResult.UnregisteredEventType:
                    logger.LogWarning("Projection {Projection}'s {EventType} has no registered schema yet; will retry", projection.Name, eventType);
                    return (CatchUpOutcome.UnregisteredEventType, 0);
                case ChangeKindResult.Forbidden:
                    return (CatchUpOutcome.Forbidden, 0);
                default:
                    throw new UnreachableException();
            }
        }

        var fromSequenceNumber = await ReadCheckpointAsync(ct);

        IAsyncEnumerable<FollowedEventEnvelope> events;
        switch (await followClient.ConnectAsync(eventType, options.Value.AppId, fromSequenceNumber, ct))
        {
            case FollowConnectResult.Connected(var connectedEvents):
                events = connectedEvents;
                break;
            case FollowConnectResult.UnregisteredEventType:
                logger.LogWarning("Projection {Projection}'s {EventType} has no registered schema yet; will retry", projection.Name, eventType);
                return (CatchUpOutcome.UnregisteredEventType, 0);
            case FollowConnectResult.Forbidden:
                // A RequiredClaims Read-direction gate this projection's own
                // token doesn't satisfy -- not a failure that should crash
                // the whole host or its other, unrelated event-type
                // connections (FollowClient's own original comment).
                return (CatchUpOutcome.Forbidden, 0);
            case FollowConnectResult.ValidationFailed(var detail):
                // This client's own request shape (mode/fromSequenceNumber)
                // is always valid -- a genuinely unexpected outcome, a real
                // bug, not a routine branch.
                throw new InvalidOperationException($"Follow connect validation failed unexpectedly for {eventType}: {detail}");
            default:
                throw new UnreachableException();
        }

        using var idleTimeoutCts = idleTimeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource();
        using var linkedCts = idleTimeoutCts is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, idleTimeoutCts.Token);
        var effectiveCt = linkedCts?.Token ?? ct;

        var consumed = 0;
        var enumerator = events.GetAsyncEnumerator(effectiveCt);
        try
        {
            while (consumed < maxEventsToConsume)
            {
                idleTimeoutCts?.CancelAfter(idleTimeout);
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (idleTimeoutCts?.IsCancellationRequested == true)
                {
                    break; // idle timeout elapsed with no new event -- not a real error
                }
                if (!hasNext)
                    break; // the connection closed

                await ApplyAsync(eventType, changeKind, enumerator.Current, ct);
                consumed++;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        return (CatchUpOutcome.Completed, consumed);
    }

    private async Task<long> ReadCheckpointAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectionsDbContext>();
        var checkpoint = await db.Checkpoints.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectionName == projection.Name, ct);
        return checkpoint?.LastSequenceNumber ?? 0;
    }

    private async Task ApplyAsync(string eventType, ChangeKind changeKind, FollowedEventEnvelope envelope, CancellationToken ct)
    {
        await _applyLock.WaitAsync(ct);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectionsDbContext>();

            var payload = envelope.Payload!;
            // ADR-101: the eventId-aware overload -- a projection that
            // doesn't override it just falls back to the original 2-arg
            // GetKey, unchanged.
            var key = projection.GetKey(eventType, envelope.EventId, payload);

            var snapshotRow = await db.Snapshots.SingleOrDefaultAsync(s => s.ProjectionName == projection.Name && s.Key == key, ct);
            var existingSnapshot = snapshotRow is null ? null : JsonNode.Parse(snapshotRow.SnapshotJson);
            var mergedSnapshot = SnapshotMerger.Merge(changeKind, existingSnapshot, payload);

            if (snapshotRow is null)
            {
                snapshotRow = new ProjectionSnapshot { ProjectionName = projection.Name, Key = key };
                db.Snapshots.Add(snapshotRow);
            }
            snapshotRow.SnapshotJson = mergedSnapshot.ToJsonString();
            snapshotRow.LastAppliedSequenceNumber = envelope.SequenceNumber;

            var readModel = projection.Project(key, mergedSnapshot);
            var existingReadModel = await db.Set<TReadModel>().FindAsync([key], ct);
            if (readModel is null)
            {
                // ADR-101: null means "no row for this key right now" --
                // delete one if it exists (EventStore.Flows.FlowProjection's
                // own use: a task that's just been resolved). Every existing
                // projection never returns null, so this branch is
                // unreachable for them.
                if (existingReadModel is not null)
                    db.Set<TReadModel>().Remove(existingReadModel);
            }
            else if (existingReadModel is null)
                db.Set<TReadModel>().Add(readModel);
            else
                db.Entry(existingReadModel).CurrentValues.SetValues(readModel);

            var checkpoint = await db.Checkpoints.SingleOrDefaultAsync(c => c.ProjectionName == projection.Name, ct);
            if (checkpoint is null)
                db.Checkpoints.Add(new ProjectionCheckpoint { ProjectionName = projection.Name, LastSequenceNumber = envelope.SequenceNumber });
            else if (envelope.SequenceNumber > checkpoint.LastSequenceNumber)
                checkpoint.LastSequenceNumber = envelope.SequenceNumber;

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _applyLock.Release();
        }
    }
}
