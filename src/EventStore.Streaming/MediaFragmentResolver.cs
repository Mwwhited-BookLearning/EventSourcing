using EventStore.Domain.Streaming;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Streaming;

// ADR-031 -- resolves a Media Fragments URI temporal fragment against a
// channel's own recorded samples: "seconds" are relative to that channel's
// earliest ingested sample, the same reference point a deep-link consumer
// (a video player's scrub bar) would seek within. Returns the interconvertible
// TelemetryPointerEntry shape directly, per this ADR's own "trivially
// interconvertible" framing.
public class MediaFragmentResolver(EventStoreContext db)
{
    public async Task<TelemetryPointerEntry?> ResolveAsync(string channelId, string fragment, CancellationToken ct = default)
    {
        if (!MediaFragmentUri.TryParse(fragment, out var beginSeconds, out var endSeconds))
            return null;

        var channel = await db.TelemetryChannels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel is null)
            return null;

        // SQLite's EF provider can't translate MIN/MAX over DateTimeOffset --
        // the same limitation TelemetryTailReader's own cursor lookups work
        // around; client-side aggregation here too.
        var timestamps = await db.TelemetrySamples.AsNoTracking().Where(s => s.ChannelId == channelId).Select(s => s.Timestamp).ToListAsync(ct);
        if (timestamps.Count == 0)
            return null;
        var start = timestamps.Min();

        var from = start.AddSeconds(beginSeconds);
        var to = endSeconds is { } end ? start.AddSeconds(end) : (DateTimeOffset?)null;
        return new TelemetryPointerEntry(channelId, channel.ThreadId, from, to);
    }
}
