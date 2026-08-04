using System.Text.Json;
using EventStore.Domain.EventLog;
using EventStore.Domain.Streaming;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Streaming Channels" (docs/08-build-plan.md),
// mirroring docs/features/streaming-channels.md's own Gherkin. Unlike every
// write-side StoredEvent-based item's tests, most of these exercise a
// deliberately SEPARATE data plane (ADR-031) -- TelemetryChannel/
// TelemetrySample -- so "never touches the event log" is itself part of
// what several scenarios assert, not just an assumption.
internal static class StreamingScenarioAssertions
{
    private static Task<RegisterChannelResult> RegisterRawScalarChannel(
        ChannelRegistryService registry, string channelId, string appId, string entityId,
        long sampleIntervalMicros = 4000, string? threadId = null, string? requiredReadClaim = null) =>
        registry.RegisterAsync(channelId, new RegisterChannelRequest(
            AppId: appId, EntityId: entityId, ContentKind: "RawScalar", SampleType: "Float64",
            MimeType: null, SampleIntervalMicros: sampleIntervalMicros, Origin: "Origin",
            ThreadId: threadId, SourceChannelIds: null, TransformKind: null, RequiredReadClaim: requiredReadClaim));

    public static async Task ABatchOfSamplesIngestsWithoutTouchingSchemaValidationHashChainOrEntityStoreFold(
        ChannelRegistryService registry, TelemetrySampleWriter writer, EventStoreContext db)
    {
        const string appId = "streaming-demo-1";
        const string channelId = "streaming-demo-1";
        await RegisterRawScalarChannel(registry, channelId, appId, "patient:1");

        var result = await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 4000,
            Values: [0.12, 0.15, 0.11, 0.20], Samples: null));

        Assert.IsInstanceOfType<IngestSamplesResult.Accepted>(result);
        var accepted = (IngestSamplesResult.Accepted)result;
        Assert.AreEqual(4, accepted.SamplesWritten);

        var samples = await db.TelemetrySamples.AsNoTracking().Where(s => s.ChannelId == channelId).ToListAsync();
        Assert.AreEqual(4, samples.Count);

        Assert.AreEqual(0, await db.Events.CountAsync(e => e.AppId == appId), "ingestion must never touch the event log");
        Assert.AreEqual(0, await db.EntityStore.CountAsync(r => r.EntityId == "patient:1"), "ingestion must never fold into the Entity Store");
    }

    public static async Task ADetectorPublishingAnEventWithATelemetryPointerRoundTripsThroughTheNormalPublishPipelineUnchanged(
        SchemaRegistryService schemaRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "streaming-demo-2";
        await schemaRegistry.RegisterAsync("DizzinessReported", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Note": { "type": "string" } }, "required": ["Note"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: null,
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var pointer = new List<TelemetryPointerEntry> { new("eeg-ch1", null, DateTimeOffset.Parse("2026-07-29T10:00:04Z"), null) };
        var result = await publish.PublishAsync("DizzinessReported",
            new PublishEventRequest(appId, 1, """{ "Note": "patient reported dizziness" }""", null, null, null, pointer),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        var accepted = (PublishResult.Accepted)result;
        Assert.AreEqual("received", accepted.Status, "the normal publish pipeline is unaffected by carrying a TelemetryPointer");

        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        var deserialized = JsonSerializer.Deserialize<List<TelemetryPointerEntry>>(stored.TelemetryPointer!)!;
        Assert.AreEqual(1, deserialized.Count);
        Assert.AreEqual("eeg-ch1", deserialized[0].ChannelId);
    }

    public static async Task ACorrelatedMultiChannelDetectionCarriesOneTelemetryPointerEntryPerContributingChannel(
        SchemaRegistryService schemaRegistry, PublishService publish, EventStoreContext db)
    {
        const string appId = "streaming-demo-2b";
        await schemaRegistry.RegisterAsync("DizzinessReported", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Note": { "type": "string" } }, "required": ["Note"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: null,
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var t0 = DateTimeOffset.Parse("2026-07-29T10:00:01Z");
        var t1 = DateTimeOffset.Parse("2026-07-29T10:00:02Z");
        var pointer = new List<TelemetryPointerEntry>
        {
            new("eeg-ch2", "session-9f2", t0, t1),
            new("eeg-ch3", "session-9f2", t0, t1),
        };
        var result = await publish.PublishAsync("DizzinessReported",
            new PublishEventRequest(appId, 1, """{ "Note": "correlated pattern" }""", null, null, null, pointer),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == ((PublishResult.Accepted)result).CorrelationId);
        var deserialized = JsonSerializer.Deserialize<List<TelemetryPointerEntry>>(stored.TelemetryPointer!)!;
        Assert.AreEqual(2, deserialized.Count);
        Assert.IsTrue(deserialized.All(e => e.ThreadId == "session-9f2"));
        Assert.AreEqual("eeg-ch2", deserialized[0].ChannelId);
        Assert.AreEqual("eeg-ch3", deserialized[1].ChannelId);
    }

    public static async Task ADeliberatelyReorderedSampleSetsLateArrivalFlagWithoutMovingTheHighWaterMark(
        ChannelRegistryService registry, TelemetrySampleWriter writer, EventStoreContext db)
    {
        const string channelId = "streaming-demo-3";
        await RegisterRawScalarChannel(registry, channelId, "streaming-demo-3", "patient:3");

        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:10Z"), SampleIntervalMicros: 4000, Values: [1.0], Samples: null));

        var reorderedResult = await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: null, SampleIntervalMicros: null, Values: null,
            Samples: [new IrregularSampleRequest(DateTimeOffset.Parse("2026-07-29T10:00:05Z"), JsonSerializer.SerializeToElement(2.0))]));

        Assert.IsInstanceOfType<IngestSamplesResult.Accepted>(reorderedResult);
        Assert.AreEqual(1, ((IngestSamplesResult.Accepted)reorderedResult).LateArrivalCount);

        var lateSample = await db.TelemetrySamples.AsNoTracking().SingleAsync(s => s.ChannelId == channelId && s.Timestamp == DateTimeOffset.Parse("2026-07-29T10:00:05Z"));
        Assert.IsTrue(lateSample.LateArrivalFlag);

        var channel = await registry.GetAsync(channelId);
        Assert.AreEqual(DateTimeOffset.Parse("2026-07-29T10:00:10Z"), channel!.LastAppliedLogicalTime, "a late sample must never move the high-water mark backward");
    }

    public static async Task ASlowUploadingProducerTriggersAChannelLagDetectedEvent(
        ChannelRegistryService registry, TelemetrySampleWriter writer, EventStoreContext db)
    {
        const string appId = "streaming-demo-4";
        const string channelId = "streaming-demo-4";
        await RegisterRawScalarChannel(registry, channelId, appId, "patient:4", sampleIntervalMicros: 4000);

        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 4000, Values: [1.0], Samples: null));

        // Backdate the channel's own LastBatchReceivedAt to simulate a real gap
        // having elapsed since the previous batch, without needing this test to
        // actually sleep past the configured threshold.
        var channel = await db.TelemetryChannels.SingleAsync(c => c.ChannelId == channelId);
        channel.LastBatchReceivedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00.004Z"), SampleIntervalMicros: 4000, Values: [1.1], Samples: null));

        var lagEvent = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.AppId == appId && e.EventType == "channellagdetected");
        Assert.IsNotNull(lagEvent, "a slow-uploading producer should have triggered a ChannelLagDetected event");
        StringAssert.Contains(lagEvent.Payload, channelId);
    }

    public static async Task ASessionWithMultipleThreadIdGroupedChannelsRendersAsOneGroupedViewNotNUnrelatedOnes(
        ChannelRegistryService registry, TelemetrySampleWriter writer, TelemetryTailReader reader, EventStoreContext db)
    {
        const string threadId = "streaming-session-1";
        await RegisterRawScalarChannel(registry, "streaming-demo-5a", "streaming-demo-5", "patient:5", threadId: threadId);
        await RegisterRawScalarChannel(registry, "streaming-demo-5b", "streaming-demo-5", "patient:5", threadId: threadId);

        await writer.IngestAsync("streaming-demo-5a", new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 1_000_000, Values: [1.0, 2.0], Samples: null));
        await writer.IngestAsync("streaming-demo-5b", new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 1_000_000, Values: [3.0, 4.0], Samples: null));

        var result = await reader.ConnectByThreadIdAsync(threadId, "Replay", DateTimeOffset.MinValue, TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<TelemetryTailResult.Connected>(result);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collected = new List<TelemetrySampleView>();
        await foreach (var sample in ((TelemetryTailResult.Connected)result).Samples.WithCancellation(cts.Token))
        {
            collected.Add(sample);
            if (collected.Count == 4)
                break;
        }

        Assert.AreEqual(4, collected.Count);
        Assert.IsTrue(collected.Any(s => s.ChannelId == "streaming-demo-5a"));
        Assert.IsTrue(collected.Any(s => s.ChannelId == "streaming-demo-5b"), "one grouped session view must include every channel sharing the ThreadId, not just one");
    }

    public static async Task AFollowerLackingARedactedRangesRequiredClaimReceivesTheSubstitutionPlusTheSidebandExistenceFlag(
        ChannelRegistryService registry, TelemetrySampleWriter writer, TelemetryTailReader reader, EventStoreContext db)
    {
        const string channelId = "streaming-demo-6";
        await RegisterRawScalarChannel(registry, channelId, "streaming-demo-6", "patient:6");

        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 1_000_000,
            Values: [1.0, 2.0, 3.0], Samples: null));

        db.RedactedRanges.Add(new RedactedRange
        {
            ChannelId = channelId,
            FromTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:01Z"),
            ToTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:01Z"),
            RequiredClaim = "clinical:full-eeg",
            Strategy = "Default",
        });
        await db.SaveChangesAsync();

        var samples = await CollectAllAsync(reader, channelId, TestClaimsPrincipal.None, expectedCount: 3);

        var real = samples.Single(s => s.Timestamp == DateTimeOffset.Parse("2026-07-29T10:00:00Z"));
        Assert.IsFalse(real.RedactionAppliedFlag);
        Assert.AreEqual(1.0, BitConverter.ToDouble(real.Value));

        var redacted = samples.Single(s => s.Timestamp == DateTimeOffset.Parse("2026-07-29T10:00:01Z"));
        Assert.IsTrue(redacted.RedactionAppliedFlag, "a caller lacking the claim must always learn that redaction applied");
        Assert.IsTrue(redacted.Value.All(b => b == 0), "RawScalar's Default substitution is zero-fill");
        Assert.AreNotEqual(2.0, BitConverter.ToDouble(redacted.Value), "the real value must never be returned to a caller lacking the claim");
    }

    public static async Task AFollowerHoldingTheRequiredClaimReceivesTheRealContentNotTheSubstitution(
        ChannelRegistryService registry, TelemetrySampleWriter writer, TelemetryTailReader reader, EventStoreContext db)
    {
        const string channelId = "streaming-demo-7";
        await RegisterRawScalarChannel(registry, channelId, "streaming-demo-7", "patient:7");

        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 1_000_000,
            Values: [1.0, 2.0, 3.0], Samples: null));

        db.RedactedRanges.Add(new RedactedRange
        {
            ChannelId = channelId,
            FromTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:01Z"),
            ToTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:01Z"),
            RequiredClaim = "clinical:full-eeg",
            Strategy = "Default",
        });
        await db.SaveChangesAsync();

        var samples = await CollectAllAsync(reader, channelId, TestClaimsPrincipal.With("clinical:full-eeg"), expectedCount: 3);

        Assert.IsTrue(samples.All(s => !s.RedactionAppliedFlag));
        var real = samples.Single(s => s.Timestamp == DateTimeOffset.Parse("2026-07-29T10:00:01Z"));
        Assert.AreEqual(2.0, BitConverter.ToDouble(real.Value));
    }

    public static async Task ARedactedRangeConfiguredForPartialRevealSubstitutesAFormatPreservingPartialValue(
        ChannelRegistryService registry, TelemetrySampleWriter writer, TelemetryTailReader reader, EventStoreContext db)
    {
        const string channelId = "streaming-demo-8";
        await registry.RegisterAsync(channelId, new RegisterChannelRequest(
            AppId: "streaming-demo-8", EntityId: "device:1", ContentKind: "RawBinary", SampleType: null,
            MimeType: null, SampleIntervalMicros: null, Origin: "Origin",
            ThreadId: null, SourceChannelIds: null, TransformKind: null, RequiredReadClaim: null));

        var ssn = "123-45-1234"u8.ToArray();
        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: null, SampleIntervalMicros: null, Values: null,
            Samples: [new IrregularSampleRequest(DateTimeOffset.Parse("2026-07-29T10:00:00Z"), JsonSerializer.SerializeToElement(Convert.ToBase64String(ssn)))]));

        db.RedactedRanges.Add(new RedactedRange
        {
            ChannelId = channelId,
            FromTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:00Z"),
            ToTimestamp = DateTimeOffset.Parse("2026-07-29T10:00:00Z"),
            RequiredClaim = "pii:view",
            Strategy = "PartialReveal",
            ShowFirst = 0,
            ShowLast = 4,
            PreserveSeparators = true,
        });
        await db.SaveChangesAsync();

        var samples = await CollectAllAsync(reader, channelId, TestClaimsPrincipal.None, expectedCount: 1);
        var revealed = System.Text.Encoding.UTF8.GetString(samples[0].Value);
        Assert.AreEqual("XXX-XX-1234", revealed);
        Assert.IsTrue(samples[0].RedactionAppliedFlag);
    }

    public static async Task ADerivedChannelIsResampledFromItsSourceChannel(
        ChannelRegistryService registry, TelemetrySampleWriter writer, EventStoreContext db)
    {
        const string sourceChannelId = "streaming-demo-9-source";
        const string derivedChannelId = "streaming-demo-9-derived";
        await RegisterRawScalarChannel(registry, sourceChannelId, "streaming-demo-9", "patient:9", sampleIntervalMicros: 4000);
        await registry.RegisterAsync(derivedChannelId, new RegisterChannelRequest(
            AppId: "streaming-demo-9", EntityId: "patient:9", ContentKind: "RawScalar", SampleType: "Float64",
            MimeType: null, SampleIntervalMicros: 1_000_000, Origin: "Derived",
            ThreadId: null, SourceChannelIds: [sourceChannelId], TransformKind: "Resample", RequiredReadClaim: null));

        var values = Enumerable.Range(0, 250).Select(i => (double)i).ToList();
        await writer.IngestAsync(sourceChannelId, new IngestSamplesRequest(
            StartTimestamp: DateTimeOffset.Parse("2026-07-29T10:00:00Z"), SampleIntervalMicros: 4000, Values: values, Samples: null));

        await ChannelDerivationWorker.RunOnceAsync(db);

        var derivedSamples = await db.TelemetrySamples.AsNoTracking().Where(s => s.ChannelId == derivedChannelId).ToListAsync();
        Assert.AreEqual(1, derivedSamples.Count, "250 samples at 4000us spanning under 1 second resample to exactly one 1Hz output sample");
        Assert.AreEqual(249.0, BitConverter.ToDouble(derivedSamples[0].Value), "decimation keeps the last source sample observed in the bucket");
    }

    public static async Task ADeepLinkTemporalFragmentResolvesToTheSameWindowAsATelemetryPointer(
        ChannelRegistryService registry, TelemetrySampleWriter writer, MediaFragmentResolver fragmentResolver, EventStoreContext db)
    {
        const string channelId = "streaming-demo-10";
        await registry.RegisterAsync(channelId, new RegisterChannelRequest(
            AppId: "streaming-demo-10", EntityId: "cam:1", ContentKind: "Media", SampleType: null,
            MimeType: "video/h264", SampleIntervalMicros: null, Origin: "Origin",
            ThreadId: null, SourceChannelIds: null, TransformKind: null, RequiredReadClaim: null));

        var start = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        await writer.IngestAsync(channelId, new IngestSamplesRequest(
            StartTimestamp: null, SampleIntervalMicros: null, Values: null,
            Samples: [new IrregularSampleRequest(start, JsonSerializer.SerializeToElement(Convert.ToBase64String([1, 2, 3])))]));

        var resolved = await fragmentResolver.ResolveAsync(channelId, "#t=10,20");
        Assert.IsNotNull(resolved);
        Assert.AreEqual(channelId, resolved!.ChannelId);
        Assert.AreEqual(start.AddSeconds(10), resolved.FromTimestamp);
        Assert.AreEqual(start.AddSeconds(20), resolved.ToTimestamp);
    }

    private static async Task<List<TelemetrySampleView>> CollectAllAsync(
        TelemetryTailReader reader, string channelId, System.Security.Claims.ClaimsPrincipal user, int expectedCount)
    {
        var result = await reader.ConnectAsync(channelId, "Replay", DateTimeOffset.MinValue, user);
        Assert.IsInstanceOfType<TelemetryTailResult.Connected>(result);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collected = new List<TelemetrySampleView>();
        await foreach (var sample in ((TelemetryTailResult.Connected)result).Samples.WithCancellation(cts.Token))
        {
            collected.Add(sample);
            if (collected.Count == expectedCount)
                break;
        }
        return collected;
    }
}
