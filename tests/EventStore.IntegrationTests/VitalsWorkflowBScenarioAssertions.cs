using EventStore.Domain.Streaming;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Vitals;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Vitals proving-ground sample's Workflow B --
// Device Monitoring -> Adverse Event Review, in-process half (device
// pairing, channel provisioning, ingestion, capture, review). The
// delegated "secondary opinion" access half (ADR-043) is a genuinely
// separate, cross-process HTTP mechanism -- see
// VitalsWorkflowBSecondaryOpinionHttpSqliteTests.cs.
internal static class VitalsWorkflowBScenarioAssertions
{
    private const string AppId = VitalsWorkflowB.AppId;

    public static async Task ACoordinatorPairsABedsideMonitorViaWebHidOnAChromiumBrowser(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowB.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("DeviceOnboarded",
            new PublishEventRequest(AppId, 1, """{ "DeviceId": "dev-0091", "DeviceModel": "VitalSync VS-200", "InterfaceKind": "WebHid", "PairedToSubjectId": "S-0091", "SiteId": "04-221" }""", null, null),
            TestClaimsPrincipal.None);
        Assert.AreEqual("accepted", result.AuthorityStatus);

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:device:dev-0091");
        Assert.IsTrue(row.Data.Contains("S-0091"));
    }

    public static async Task ACoordinatorPairsTheSameClassOfDeviceViaTheNativeBridgeFallbackOnFirefox(
        SchemaRegistryService registry, PublishService publish)
    {
        await VitalsWorkflowB.RegisterAsync(registry);

        var result = await publish.PublishAsync("DeviceOnboarded",
            new PublishEventRequest(AppId, 1, """{ "DeviceId": "dev-0044", "DeviceModel": "VitalSync VS-200", "InterfaceKind": "NativeBridge", "PairedToSubjectId": "S-0044", "SiteId": "04-221" }""", null, null),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result, "WebHID/Serial/Bluetooth ship in Chromium only (ADR-070) -- NativeBridge is what makes Firefox/Safari pairing possible at all");
    }

    public static async Task AnOriginTelemetryChannelIsProvisionedScopedToThePatientEntity(ChannelRegistryService channelRegistry)
    {
        var result = await channelRegistry.RegisterAsync("vitals-s0091", new RegisterChannelRequest(
            AppId: AppId, EntityId: $"{AppId}:patient:S-0091", ContentKind: "RawScalar", SampleType: "Float64",
            MimeType: null, SampleIntervalMicros: 1_000_000, Origin: "Origin", ThreadId: null,
            SourceChannelIds: null, TransformKind: null, RequiredReadClaim: "telemetry:read:vitals"));

        Assert.IsInstanceOfType<RegisterChannelResult.Success>(result);
        var channel = await channelRegistry.GetAsync("vitals-s0091");
        Assert.AreEqual($"{AppId}:patient:S-0091", channel!.EntityId, "a device can be swapped mid-trial without re-provisioning the channel history (ADR-031)");
    }

    public static async Task ContinuousSamplesAreIngestedWithoutPerSampleValidationOrAnEntityStoreFold(
        SchemaRegistryService registry, PublishService publish, ChannelRegistryService channelRegistry, EventStoreContext db)
    {
        await channelRegistry.RegisterAsync("vitals-s0091b", new RegisterChannelRequest(
            AppId: AppId, EntityId: $"{AppId}:patient:S-0091", ContentKind: "RawScalar", SampleType: "Float64",
            MimeType: null, SampleIntervalMicros: 1_000_000, Origin: "Origin", ThreadId: null,
            SourceChannelIds: null, TransformKind: null, RequiredReadClaim: null));
        var writer = new TelemetrySampleWriter(db, registry, publish, Options.Create(new TelemetryIngestOptions()));

        var values = Enumerable.Repeat(98.0, 60).ToList();
        var result = await writer.IngestAsync("vitals-s0091b",
            new IngestSamplesRequest(DateTimeOffset.UtcNow, 1_000_000, values, null));

        var accepted = Assert.IsInstanceOfType<IngestSamplesResult.Accepted>(result);
        Assert.AreEqual(60, accepted.SamplesWritten);
        Assert.AreEqual(60, await db.TelemetrySamples.CountAsync(s => s.ChannelId == "vitals-s0091b"));
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId.Contains("vitals-s0091b")),
            "no JsonSchema validation, ChainHash, or Entity Store fold occurs for telemetry samples (ADR-031)");
    }

    public static async Task ADeviceLinkedAdverseEventIsCapturedNonAuthoritativelyCarryingATelemetryPointer(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowB.RegisterAsync(registry);

        var pointer = new List<TelemetryPointerEntry> { new("vitals-s0091", null, DateTimeOffset.Parse("2026-07-29T14:02:10Z"), null) };
        var result = (PublishResult.Accepted)await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(AppId, 1, """{ "AeId": "ae-1042", "SubjectId": "S-0091", "Severity": "Severe", "SeriousAdverseEvent": true }""", null, null,
                TelemetryPointer: pointer, ReviewPending: true),
            TestClaimsPrincipal.None);
        Assert.AreEqual("pending_review", result.AuthorityStatus);

        await RouterWorker.RunOnceAsync(db, registry, UpcastingTestSupport.CreateChain());
        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:adverseevent:ae-1042");
        Assert.IsTrue(liveRow.Data.Contains("Severe"));
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:adverseevent:ae-1042"),
            "a device's own uncertainty about its detection starts AuthorityStatus below accepted (ADR-042) -- the automated-detector trigger, not an identity problem");
    }

    public static async Task ASiteCoordinatorEnteredAdverseEventAlsoStartsPendingReviewViaAnExplicitMarker(
        SchemaRegistryService registry, PublishService publish)
    {
        await VitalsWorkflowB.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(AppId, 1, """{ "AeId": "ae-1039", "SubjectId": "S-0044", "Severity": "Moderate", "SeriousAdverseEvent": false }""", null, null,
                ReviewPending: true),
            TestClaimsPrincipal.None);

        Assert.AreEqual("pending_review", result.AuthorityStatus,
            "without the explicit marker this would default to accepted (ADR-042) -- ADR-006 already verified this coordinator's identity/permission synchronously");
    }

    public static async Task ThePIsReviewDecisionWithoutSufficientStepUpAuthenticationIsChallengedNotStored(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowB.RegisterAsync(registry);
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"));

        var ae = (PublishResult.Accepted)await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(AppId, 1, """{ "AeId": "ae-1042b", "SubjectId": "S-0091", "Severity": "Severe", "SeriousAdverseEvent": true }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{ae.CorrelationId}}", "decision": "accepted", "decidingActorId": "pi-7" }""", null, null,
                Meaning: "approved"),
            pi);

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        CollectionAssert.AreEqual(new[] { "urn:trial:step-up" }, stepUp.AcrValues.ToArray());
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == AppId && e.EventType == "authoritydecision" && e.EntityId == ae.CorrelationId.ToString()));
    }

    public static async Task ThePISignsOffAcceptedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowB.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        var ae = (PublishResult.Accepted)await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(AppId, 1, """{ "AeId": "ae-1042c", "SubjectId": "S-0091", "Severity": "Severe", "SeriousAdverseEvent": true }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{ae.CorrelationId}}", "decision": "accepted", "decidingActorId": "pi-7" }""", null, null,
                Meaning: "approved"),
            pi);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedDecision = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == decision.CorrelationId);
        Assert.AreEqual("pi-7", storedDecision.Signature!.SignerId);
        Assert.AreEqual("approved", storedDecision.Signature.Meaning);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == ae.CorrelationId);
        Assert.AreEqual("accepted", target.AuthorityStatus);

        var row = await db.EntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:adverseevent:ae-1042c");
        Assert.IsTrue(row.Data.Contains("Severe"), "the same 'apply once, on the triggering condition' catch-up shape ADR-042 already establishes");
    }

    public static async Task ThePISignsOffRejectedInsteadAndTheRecordNeverReachesTheAuthoritativeEntityStore(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowB.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var pi = TestClaimsPrincipal.WithClaims(("sub", "pi-7"), ("review", "ae"), ("consent", "approve"), ("acr", "urn:trial:step-up"), ("auth_time", recentAuthTime));

        var ae = (PublishResult.Accepted)await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(AppId, 1, """{ "AeId": "ae-1039b", "SubjectId": "S-0044", "Severity": "Moderate", "SeriousAdverseEvent": false }""", null, null, ReviewPending: true),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var decision = (PublishResult.Accepted)await publish.PublishAsync("authorityDecision",
            new PublishEventRequest(AppId, 1, $$"""{ "targetEventId": "{{ae.CorrelationId}}", "decision": "rejected", "decidingActorId": "pi-7", "reason": "duplicate report, same episode as ae-1030" }""", null, null,
                Meaning: "reviewed"),
            pi);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var storedDecision = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == decision.CorrelationId);
        Assert.AreEqual("reviewed", storedDecision.Signature!.Meaning);

        var target = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == ae.CorrelationId);
        Assert.AreEqual("rejected", target.AuthorityStatus);
        Assert.IsFalse(await db.EntityStore.AnyAsync(r => r.EntityId == $"{AppId}:adverseevent:ae-1039b"),
            "RejectionBehavior Annotate (default) means a rejected event was never folded into the authoritative store to begin with (ADR-042) -- nothing to compensate");

        var liveRow = await db.LiveEntityStore.AsNoTracking().SingleAsync(r => r.EntityId == $"{AppId}:adverseevent:ae-1039b");
        Assert.IsTrue(liveRow.Data.Contains("Moderate"), "ae-1039 remains visible in the Live View, re-labeled rejected, never deleted");
    }
}
