using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.Streaming;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventStore.Streaming;

// ADR-031 -- batch ingestion. Deliberately no JsonSchema check, no
// ChainHash, no Entity Store fold at all -- exactly the per-item cost this
// data plane exists to avoid. Out-of-order/late-arrival detection reuses
// ADR-029's high-water-mark mechanism per channel; slow-upload detection
// publishes a reserved ChannelLagDetected event through the completely
// normal publish path (ADR-020/023), unlike ADR-027's UpcastMaterializer --
// there is no claims-bypass concern here, since ChannelLagDetected carries
// no RequiredClaims of its own.
public class TelemetrySampleWriter(
    EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publish, IOptions<TelemetryIngestOptions> options)
{
    // No caller identity is meaningful for a system-generated lag event --
    // ChannelLagDetected declares no RequiredClaims, so an empty principal
    // is never Forbidden publishing it (RequiredClaimEvaluator.HasAny is
    // vacuously true for an empty claims list, regardless of principal).
    private static readonly ClaimsPrincipal SystemPrincipal = new(new ClaimsIdentity());

    public async Task<IngestSamplesResult> IngestAsync(string channelId, IngestSamplesRequest request, CancellationToken ct = default)
    {
        var channel = await db.TelemetryChannels.SingleOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel is null)
            return new IngestSamplesResult.ChannelNotFound();

        List<(DateTimeOffset Timestamp, long? MonotonicElapsedMicros, byte[] Value)> parsed;
        try
        {
            parsed = ParseBatch(channel, request);
        }
        catch (FormatException ex)
        {
            return new IngestSamplesResult.ValidationFailed(ex.Message);
        }

        var lateArrivalCount = 0;
        var maxTimestampThisBatch = channel.LastSampleTimestampReceived;
        foreach (var (timestamp, monotonicElapsedMicros, value) in parsed)
        {
            // ADR-029's mechanism, reused per-channel (ADR-031) -- a late
            // sample is still written, never dropped, never reordered.
            var isLate = timestamp <= channel.LastAppliedLogicalTime;
            if (isLate)
                lateArrivalCount++;
            else
                channel.LastAppliedLogicalTime = timestamp;

            if (maxTimestampThisBatch is null || timestamp > maxTimestampThisBatch)
                maxTimestampThisBatch = timestamp;

            db.TelemetrySamples.Add(new TelemetrySample
            {
                ChannelId = channelId,
                Timestamp = timestamp,
                MonotonicElapsedMicros = monotonicElapsedMicros,
                Value = value,
                LateArrivalFlag = isLate,
            });
        }

        await CheckForLagAndPublishAsync(channel, ct);

        channel.LastBatchReceivedAt = DateTimeOffset.UtcNow;
        channel.LastSampleTimestampReceived = maxTimestampThisBatch;

        await db.SaveChangesAsync(ct);

        return new IngestSamplesResult.Accepted(channelId, parsed.Count, lateArrivalCount);
    }

    // ADR-031 -- compares the gap since the channel's last received batch
    // against its own ExpectedInterArrivalInterval (SampleIntervalMicros).
    // Skipped on a channel's very first batch (LastBatchReceivedAt is null)
    // -- there is no prior receive time to measure a gap from yet.
    private async Task CheckForLagAndPublishAsync(TelemetryChannel channel, CancellationToken ct)
    {
        if (channel.LastBatchReceivedAt is not { } lastReceivedAt || channel.SampleIntervalMicros is not { } expectedIntervalMicros)
            return;

        var actualGapMicros = (DateTimeOffset.UtcNow - lastReceivedAt).TotalMicroseconds;
        var thresholdMicros = expectedIntervalMicros * options.Value.LagThresholdMultiplier;
        if (actualGapMicros <= thresholdMicros)
            return;

        await ChannelLagDetectedEventType.EnsureRegisteredAsync(schemaRegistry, channel.AppId, ct);

        var payload = JsonSerializer.Serialize(new
        {
            ChannelId = channel.ChannelId,
            ExpectedGapMicros = expectedIntervalMicros,
            ActualGapMicros = actualGapMicros,
        });
        var telemetryPointer = channel.LastSampleTimestampReceived is { } lastSampleTimestamp
            ? new List<TelemetryPointerEntry> { new(channel.ChannelId, channel.ThreadId, lastSampleTimestamp, null) }
            : null;

        await publish.PublishAsync(ChannelLagDetectedEventType.Name,
            new PublishEventRequest(channel.AppId, 1, payload, null, null, null, telemetryPointer),
            SystemPrincipal, ct);
    }

    private static List<(DateTimeOffset Timestamp, long? MonotonicElapsedMicros, byte[] Value)> ParseBatch(
        TelemetryChannel channel, IngestSamplesRequest request)
    {
        if (request.Values is { Count: > 0 } values)
        {
            if (request.StartTimestamp is not { } start || request.SampleIntervalMicros is not { } intervalMicros)
                throw new FormatException("startTimestamp and sampleIntervalMicros are required alongside values");

            return values
                .Select((v, i) => (start.AddMicroseconds(i * intervalMicros), (long?)null, ConvertScalar(channel, v)))
                .ToList();
        }

        if (request.Samples is { Count: > 0 } samples)
        {
            return samples
                .Select(s => (s.Timestamp, s.MonotonicElapsedMicros, ConvertIrregularValue(channel, s.Value)))
                .ToList();
        }

        throw new FormatException("request must carry either values (fixed-rate) or samples (irregular)");
    }

    private static byte[] ConvertIrregularValue(TelemetryChannel channel, JsonElement value) =>
        channel.ContentKind == ContentKind.RawScalar
            ? ConvertScalar(channel, value.GetDouble())
            : value.GetBytesFromBase64();

    private static byte[] ConvertScalar(TelemetryChannel channel, double value) => channel.SampleType switch
    {
        SampleType.Int32 => BitConverter.GetBytes((int)value),
        _ => BitConverter.GetBytes(value), // Float64, and the channel-registration default
    };
}
