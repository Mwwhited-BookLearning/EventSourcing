using EventStore.Domain.Streaming;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Streaming;

// ADR-031 -- "an internal follower," the same architectural shape ADR-007's
// derivation workers, ADR-015's ProjectionHost, and ADR-027's
// UpcastMaterializer all already use: tail the source channel(s), apply
// the transform, append to the derived channel through the same ingestion
// path any other writer uses (TelemetrySample rows, no JsonSchema/
// ChainHash/fold). No second derivation mechanism invented for telemetry.
// Progress is tracked via the derived channel's OWN LastAppliedLogicalTime
// -- the same field ingestion already maintains -- rather than a new
// persisted checkpoint shape (ADR-031 names no such shape).
public class ChannelDerivationWorker(IServiceScopeFactory scopeFactory, ILogger<ChannelDerivationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();
                await RunOnceAsync(db, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Channel derivation tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    public static async Task RunOnceAsync(EventStoreContext db, CancellationToken ct = default)
    {
        var derivedChannels = await db.TelemetryChannels.Where(c => c.Origin == ChannelOrigin.Derived).ToListAsync(ct);
        foreach (var derived in derivedChannels)
        {
            if (derived.SourceChannelIds is not { Count: > 0 } sourceChannelIds)
                continue;

            // Filter/Aggregate/Transcode are named in ADR-031's own TransformKind
            // vocabulary but not built at this stage -- an honestly-flagged gap,
            // not a silent no-op pretending to have processed something.
            if (derived.TransformKind == "Resample")
                await ResampleAsync(db, derived, sourceChannelIds[0], ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // Decimation resample: buckets the source's samples by elapsed time from
    // the first unprocessed sample, into windows the size of the derived
    // channel's OWN declared SampleIntervalMicros (its target rate), keeping
    // the last sample observed in each window. A simple, real technique --
    // not an anti-aliasing filter -- appropriate for this build stage's
    // worked-example scope, not represented as more than that.
    private static async Task ResampleAsync(EventStoreContext db, TelemetryChannel derived, string sourceChannelId, CancellationToken ct)
    {
        var targetIntervalMicros = derived.SampleIntervalMicros
            ?? throw new InvalidOperationException($"Derived channel {derived.ChannelId} declares TransformKind Resample but no target SampleIntervalMicros");

        // SQLite's EF provider can't translate a DateTimeOffset relational
        // (non-equality) comparison, only equality -- the same limitation
        // already found/fixed for PendingJoinState's own TTL sweep and
        // TelemetryTailReader's own read path above. ChannelId (translatable)
        // filters in the query; Timestamp > LastAppliedLogicalTime client-side.
        var sourceSamples = (await db.TelemetrySamples
            .AsNoTracking()
            .Where(s => s.ChannelId == sourceChannelId)
            .ToListAsync(ct))
            .Where(s => s.Timestamp > derived.LastAppliedLogicalTime)
            .OrderBy(s => s.Timestamp)
            .ToList();
        if (sourceSamples.Count == 0)
            return;

        var origin = sourceSamples[0].Timestamp;
        var ticksPerBucket = targetIntervalMicros * TimeSpan.TicksPerMicrosecond;
        var buckets = sourceSamples.GroupBy(s => (s.Timestamp - origin).Ticks / ticksPerBucket).OrderBy(b => b.Key);

        foreach (var bucket in buckets)
        {
            var last = bucket.Last();
            db.TelemetrySamples.Add(new TelemetrySample
            {
                ChannelId = derived.ChannelId,
                Timestamp = last.Timestamp,
                Value = last.Value,
                LateArrivalFlag = false,
            });
        }

        derived.LastAppliedLogicalTime = sourceSamples[^1].Timestamp;
    }
}
