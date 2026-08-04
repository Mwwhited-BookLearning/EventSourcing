using EventStore.Domain.Streaming;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Streaming;

// ADR-031 -- channel registration itself. Deliberately no JSON Schema, no
// version history, no active/superseded lifecycle the way EventTypeDefinition
// has: a channel's declared shape (ContentKind/SampleType/MimeType) doesn't
// evolve the way an event type's payload shape does -- a producer that needs
// a different shape registers a new ChannelId, per this ADR's own framing
// of ContentKind as fixed per channel.
public class ChannelRegistryService(EventStoreContext db)
{
    public async Task<RegisterChannelResult> RegisterAsync(string channelId, RegisterChannelRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (!Enum.TryParse<ContentKind>(request.ContentKind, ignoreCase: true, out var contentKind))
            errors.Add($"contentKind must be one of RawScalar, RawBinary, Media (got: {request.ContentKind})");

        if (!Enum.TryParse<ChannelOrigin>(request.Origin, ignoreCase: true, out var origin))
            errors.Add($"origin must be one of Origin, Derived (got: {request.Origin})");

        SampleType? sampleType = null;
        if (request.SampleType is { } sampleTypeText)
        {
            if (!Enum.TryParse<SampleType>(sampleTypeText, ignoreCase: true, out var parsed))
                errors.Add($"sampleType must be one of Float64, Int32 (got: {sampleTypeText})");
            else
                sampleType = parsed;
        }

        if (errors.Count == 0 && contentKind == ContentKind.RawScalar && sampleType is null)
            errors.Add("sampleType is required when contentKind is RawScalar");

        if (errors.Count == 0 && contentKind == ContentKind.Media && string.IsNullOrEmpty(request.MimeType))
            errors.Add("mimeType is required when contentKind is Media");

        if (errors.Count == 0 && origin == ChannelOrigin.Derived && (request.SourceChannelIds is not { Count: > 0 } || string.IsNullOrEmpty(request.TransformKind)))
            errors.Add("sourceChannelIds and transformKind are required when origin is Derived");

        if (errors.Count > 0)
            return new RegisterChannelResult.ValidationFailed(errors);

        db.TelemetryChannels.Add(new TelemetryChannel
        {
            ChannelId = channelId,
            AppId = request.AppId,
            EntityId = request.EntityId,
            ContentKind = contentKind,
            SampleType = sampleType,
            MimeType = request.MimeType,
            SampleIntervalMicros = request.SampleIntervalMicros,
            Origin = origin,
            ThreadId = request.ThreadId,
            SourceChannelIds = request.SourceChannelIds,
            TransformKind = request.TransformKind,
            RequiredReadClaim = request.RequiredReadClaim,
            LastAppliedLogicalTime = DateTimeOffset.MinValue,
        });
        await db.SaveChangesAsync(ct);

        return new RegisterChannelResult.Success();
    }

    public Task<TelemetryChannel?> GetAsync(string channelId, CancellationToken ct = default) =>
        db.TelemetryChannels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId, ct);
}
