using System.Runtime.CompilerServices;
using System.Security.Claims;
using EventStore.Domain.SchemaRegistry;
using EventStore.Domain.Streaming;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Streaming;

// ADR-031's tail/replay reuses ADR-010's Follow shape directly, applied to
// TelemetrySample instead of StoredEvent -- one continuous poll loop, only
// the initial cursor differs between mode=tail (default, new samples only)
// and mode=replay&fromTimestamp=<t> (historical, then continuing live with
// no gap). ADR-081's ThreadId-scoped session view is the same mechanism
// applied across every channel sharing one ThreadId, presented as one
// grouped stream rather than N unrelated ones.
public class TelemetryTailReader(EventStoreContext db, StreamRedactionResolver redactionResolver)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<TelemetryTailResult> ConnectAsync(
        string channelId, string? mode, DateTimeOffset? fromTimestamp, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var channel = await db.TelemetryChannels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel is null)
            return new TelemetryTailResult.ChannelNotFound();

        if (channel.RequiredReadClaim is { } claim && !RequiredClaimEvaluator.HasClaim(user, claim))
            return new TelemetryTailResult.Forbidden();

        if (!TryResolveMode(mode, fromTimestamp, out var replay, out var validationError))
            return new TelemetryTailResult.ValidationFailed(validationError!);

        var lastSeen = replay
            ? fromTimestamp ?? DateTimeOffset.MinValue
            : await CurrentMaxTimestampAsync([channelId], ct);

        var samples = TailAsync([channel], lastSeen, user, ct);
        return new TelemetryTailResult.Connected(samples);
    }

    // ADR-081 -- "every event pointing into one recording session" reuses
    // ThreadId as denormalized grouping; the read side's own equivalent is
    // grouping every CHANNEL sharing one ThreadId into a single tail.
    public async Task<TelemetryTailResult> ConnectByThreadIdAsync(
        string threadId, string? mode, DateTimeOffset? fromTimestamp, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var channels = await db.TelemetryChannels.AsNoTracking().Where(c => c.ThreadId == threadId).ToListAsync(ct);
        if (channels.Count == 0)
            return new TelemetryTailResult.ChannelNotFound();

        var deniedClaim = channels.Select(c => c.RequiredReadClaim).FirstOrDefault(claim => claim is not null && !RequiredClaimEvaluator.HasClaim(user, claim));
        if (deniedClaim is not null)
            return new TelemetryTailResult.Forbidden();

        if (!TryResolveMode(mode, fromTimestamp, out var replay, out var validationError))
            return new TelemetryTailResult.ValidationFailed(validationError!);

        var channelIds = channels.Select(c => c.ChannelId).ToList();
        var lastSeen = replay
            ? fromTimestamp ?? DateTimeOffset.MinValue
            : await CurrentMaxTimestampAsync(channelIds, ct);

        var samples = TailAsync(channels, lastSeen, user, ct);
        return new TelemetryTailResult.Connected(samples);
    }

    // SQLite's EF provider can't translate MIN/MAX over DateTimeOffset --
    // the same limitation this class's own poll query already works around;
    // client-side aggregation here too. mode: Tail's own "start from now"
    // cursor -- an empty channel (no samples yet) starts from MinValue, the
    // same "nothing to skip" starting point Follow's own EventTailReader
    // uses (MaxAsync(SequenceNumber) ?? 0) for an empty event log.
    private async Task<DateTimeOffset> CurrentMaxTimestampAsync(List<string> channelIds, CancellationToken ct)
    {
        var timestamps = await db.TelemetrySamples.AsNoTracking().Where(s => channelIds.Contains(s.ChannelId)).Select(s => s.Timestamp).ToListAsync(ct);
        return timestamps.Count > 0 ? timestamps.Max() : DateTimeOffset.MinValue;
    }

    private static bool TryResolveMode(string? mode, DateTimeOffset? fromTimestamp, out bool replay, out string? error)
    {
        replay = false;
        error = null;
        if (mode is not null && !string.Equals(mode, "Tail", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "Replay", StringComparison.OrdinalIgnoreCase))
        {
            error = $"mode must be \"Tail\" or \"Replay\" (got: {mode})";
            return false;
        }
        replay = string.Equals(mode, "Replay", StringComparison.OrdinalIgnoreCase);
        if (fromTimestamp is not null && !replay)
        {
            error = "fromTimestamp is only valid alongside mode: Replay";
            return false;
        }
        return true;
    }

    private async IAsyncEnumerable<TelemetrySampleView> TailAsync(
        List<TelemetryChannel> channels, DateTimeOffset lastSeen, ClaimsPrincipal user, [EnumeratorCancellation] CancellationToken ct)
    {
        var channelIds = channels.Select(c => c.ChannelId).ToList();
        var channelsById = channels.ToDictionary(c => c.ChannelId);

        while (!ct.IsCancellationRequested)
        {
            // SQLite's EF provider can't translate a DateTimeOffset relational
            // (non-equality) comparison, only equality -- the same limitation
            // already found/fixed for PendingJoinState's own TTL sweep. Filters
            // by ChannelId (translatable) in the query, then Timestamp > lastSeen
            // client-side.
            var matching = (await db.TelemetrySamples
                .AsNoTracking()
                .Where(s => channelIds.Contains(s.ChannelId))
                .ToListAsync(ct))
                .Where(s => s.Timestamp > lastSeen)
                .OrderBy(s => s.Timestamp)
                .ToList();

            if (matching.Count > 0)
            {
                // Small, per-channel-set list -- overlap-checked in memory
                // rather than pushed into the query, matching count and
                // rate this data plane is scoped for (ADR-031).
                var ranges = await db.RedactedRanges.AsNoTracking().Where(r => channelIds.Contains(r.ChannelId)).ToListAsync(ct);

                foreach (var sample in matching)
                {
                    yield return ApplyRedaction(channelsById[sample.ChannelId], sample, ranges, user);
                    lastSeen = sample.Timestamp;
                }
            }

            if (matching.Count == 0)
                await Task.Delay(PollInterval, ct);
        }
    }

    // ADR-052 -- read-time, not materialized: TelemetrySample rows on disk
    // are never touched. A caller who already holds (or later acquires)
    // the RequiredClaim sees the real value; every other caller sees the
    // resolved substitution plus the sideband existence flag, never a
    // response indistinguishable from "no redaction happened here."
    private TelemetrySampleView ApplyRedaction(TelemetryChannel channel, Domain.Streaming.TelemetrySample sample, List<RedactedRange> ranges, ClaimsPrincipal user)
    {
        var overlapping = ranges.FirstOrDefault(r => sample.Timestamp >= r.FromTimestamp && sample.Timestamp <= r.ToTimestamp);
        if (overlapping is null || RequiredClaimEvaluator.HasClaim(user, overlapping.RequiredClaim))
            return new TelemetrySampleView(channel.ChannelId, sample.Timestamp, sample.Value, sample.LateArrivalFlag, RedactionAppliedFlag: false);

        var strategy = redactionResolver.Resolve(channel, overlapping);
        var substituted = strategy.Redact(sample.Value, overlapping);
        return new TelemetrySampleView(channel.ChannelId, sample.Timestamp, substituted, sample.LateArrivalFlag, RedactionAppliedFlag: true);
    }
}
