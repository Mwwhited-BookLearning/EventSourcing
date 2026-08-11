using EventStore.Erasure;
using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Samples.Vitals;

namespace EventStore.IntegrationTests;

// Shared scenarios for the Vitals proving-ground sample's Workflow C --
// Trial Data Export & Subject Rights (docs/domains/clinical-trials-
// device-telemetry/features/trial-data-export-and-subject-rights.md),
// erasure half. The export/playback half is deliberately NOT re-proven
// here -- ADR-068's own mechanism (LineageExportQueries/
// BitemporalPlaybackService, scope-gated on "events:lineage:read", never
// the feature doc's own invented "export:lineage"/"export:playback"
// claim names) is already fully exercised generically in
// LineageExportHttpSqliteTests.cs; this domain contributes no new risk
// to that mechanism, only different entity/event type names, so building
// a second, redundant HTTP test for it here would prove nothing new
// (docs/domains/README.md's own build-status note records this scope
// decision explicitly).
//
// Reuses the exact "Follow + IPayloadMasker, not a generic entity read"
// pattern ErasureScenarioAssertions.cs already established for the core
// engine -- confirmed while scoping this workflow that no GraphQL field
// for reading a whole entity's current state (masked or otherwise)
// exists at all ("GraphQL-Only Query Layer"'s own build-scope note).
internal static class VitalsWorkflowCScenarioAssertions
{
    private const string AppId = VitalsWorkflowC.AppId;

    private static async Task<System.Text.Json.Nodes.JsonNode> FollowPatientScreenedAsync(FollowService follow, string subjectId)
    {
        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            "PatientScreened", new FollowRequest(AppId, Filter: null, Mode: "Replay", FromSequenceNumber: 0),
            TestClaimsPrincipal.With("clearance:phi"), cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        System.Text.Json.Nodes.JsonNode? match = null;
        for (var i = 0; i < 20 && match is null; i++)
        {
            var moveNext = enumerator.MoveNextAsync().AsTask();
            var winner = await Task.WhenAny(moveNext, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            if (winner != moveNext || !await moveNext)
                break;
            if (enumerator.Current.MaskedPayload!["SubjectId"]?.GetValue<string>() == subjectId)
                match = enumerator.Current.MaskedPayload;
        }
        cts.Cancel();
        Assert.IsNotNull(match, $"never saw a PatientScreened event for {subjectId}");
        return match!;
    }

    public static async Task AWithdrawnSubjectsConsentWithdrawalIsRetainedForeverNeverItselfErased(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        await VitalsWorkflowC.RegisterAsync(registry);

        var result = (PublishResult.Accepted)await publish.PublishAsync("ConsentWithdrawn",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0077", "WithdrawnAt": "2026-07-28T00:00:00Z", "Reason": "subject withdrew consent" }""", null, null),
            TestClaimsPrincipal.None);

        Assert.IsNotNull(result);
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == AppId && e.EventType == "consentwithdrawn");
        Assert.IsFalse(string.IsNullOrEmpty(stored.ChainHash), "the withdrawal itself is a real trial event, hash-chained like any other, retained forever per ICH-GCP");
    }

    public static async Task ADataProtectionOfficerRequestsErasureForTheWithdrawnSubjectDestroyingTheEncryptionKey(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ErasureKeyService erasureKeyService)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        await VitalsWorkflowC.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0077b", "SiteId": "04-221", "EligibilityStatus": "Eligible", "LegalName": "Jordan Doe", "DateOfBirth": "1980-01-01" }""", null, null),
            TestClaimsPrincipal.WithClaims(("patient", "enroll")));
        await publish.PublishAsync("ConsentWithdrawn",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0077b", "WithdrawnAt": "2026-07-28T00:00:00Z", "Reason": "subject withdrew consent" }""", null, null),
            TestClaimsPrincipal.None);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        var entityId = $"{AppId}:patient:S-0077b";
        var result = (PublishResult.Accepted)await publish.PublishAsync("EntityErasureRequested",
            new PublishEventRequest(AppId, 1, $$"""{ "TargetEntityId": "{{entityId}}" }""", null, null),
            TestClaimsPrincipal.With("erasure:request"));
        Assert.IsNotNull(result);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        var key = await db.EntityErasureKeys.AsNoTracking().SingleAsync(k => k.EntityId == entityId);
        Assert.IsNotNull(key.ErasedAt, "EntityErasureRequested must irreversibly destroy the DEK");

        var erasureEvent = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == AppId && e.EventType == "entityerasurerequested");
        Assert.IsFalse(string.IsNullOrEmpty(erasureEvent.ChainHash), "the erasure request itself is retained and hash-chained forever -- only the DEK, never StoredEvent.Payload or the chain, is touched");
    }

    public static async Task AfterErasurePhiFieldsRenderErasedWhileStructuralFieldsRemainReadable(
        SchemaRegistryService registry, PublishService publish, FollowService follow, EventStoreContext db, ErasureKeyService erasureKeyService)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        await VitalsWorkflowC.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0077c", "SiteId": "04-221", "EligibilityStatus": "Eligible", "LegalName": "Jordan Doe", "DateOfBirth": "1980-01-01" }""", null, null),
            TestClaimsPrincipal.WithClaims(("patient", "enroll")));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        var beforeErasure = await FollowPatientScreenedAsync(follow, "S-0077c");
        Assert.AreEqual("Jordan Doe", beforeErasure["LegalName"]!["value"]!.GetValue<string>(), "a clearance:phi holder sees the real decrypted value before erasure");

        var entityId = $"{AppId}:patient:S-0077c";
        await publish.PublishAsync("EntityErasureRequested",
            new PublishEventRequest(AppId, 1, $$"""{ "TargetEntityId": "{{entityId}}" }""", null, null),
            TestClaimsPrincipal.With("erasure:request"));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        var afterErasure = await FollowPatientScreenedAsync(follow, "S-0077c");
        Assert.IsTrue(afterErasure["LegalName"]!["erased"]?.GetValue<bool>() ?? false,
            "\"erased\" is distinct from \"masked\" (ADR-057) -- no claim, however privileged, can ever restore this once the DEK is destroyed");
        Assert.AreEqual("04-221", afterErasure["SiteId"]!.GetValue<string>(), "SiteId is not x-masking-classified PHI -- it remains fully readable regardless of claims or erasure");
        Assert.AreEqual("Eligible", afterErasure["EligibilityStatus"]!.GetValue<string>());
    }

    public static async Task ACallerWithoutTheErasureRequestClaimCannotDestroyAnotherSubjectsKey(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ErasureKeyService erasureKeyService)
    {
        await VitalsWorkflowA.RegisterAsync(registry);
        var upcastChain = UpcastingTestSupport.CreateChain();

        await publish.PublishAsync("PatientScreened",
            new PublishEventRequest(AppId, 1, """{ "SubjectId": "S-0091", "SiteId": "04-221", "EligibilityStatus": "Eligible", "LegalName": "Continuity Patient", "DateOfBirth": "1975-05-05" }""", null, null),
            TestClaimsPrincipal.WithClaims(("patient", "enroll")));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        var entityId = $"{AppId}:patient:S-0091";
        var result = await publish.PublishAsync("EntityErasureRequested",
            new PublishEventRequest(AppId, 1, $$"""{ "TargetEntityId": "{{entityId}}" }""", null, null),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Forbidden>(result, "S-0091's own continuity is never put at risk -- a caller lacking erasure:request must be rejected before any key is ever touched");
        var key = await db.EntityErasureKeys.AsNoTracking().SingleOrDefaultAsync(k => k.EntityId == entityId);
        Assert.IsTrue(key is null || key.ErasedAt is null, "S-0091's own data-encryption key must remain intact");
    }
}
