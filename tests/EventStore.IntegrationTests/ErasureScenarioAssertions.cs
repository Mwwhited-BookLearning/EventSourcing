using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Erasure;
using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "GDPR/CCPA Erasure via Crypto-Shredding"
// (docs/08-build-plan.md, ADR-057). Every scenario runs the full real
// pipeline -- PublishService's real PayloadEncryptor, RouterWorker's real
// fold (needed so StoredEvent.EntityId is actually resolved before a
// decrypt/erasure-scope lookup keyed on it can mean anything), then Follow's
// real PayloadMasker -- rather than calling IErasureKeyStore/ErasureKeyService
// directly, so what's verified is the end-to-end behavior ADR-057 actually
// promises, not just its individual pieces in isolation.
internal static class ErasureScenarioAssertions
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(10);

    private static async Task<List<FollowedEvent>> Collect(IAsyncEnumerator<FollowedEvent> enumerator, int count, CancellationTokenSource cts)
    {
        var results = new List<FollowedEvent>();
        for (var i = 0; i < count; i++)
        {
            var moveNext = enumerator.MoveNextAsync().AsTask();
            var winner = await Task.WhenAny(moveNext, Task.Delay(PerItemTimeout, cts.Token));
            if (winner != moveNext)
            {
                cts.Cancel();
                Assert.Fail($"Timed out waiting for item {i + 1} of {count}");
            }
            Assert.IsTrue(await moveNext, $"stream ended after {i} of {count} expected items");
            results.Add(enumerator.Current);
        }
        return results;
    }

    private static async Task<JsonNode> FollowOneEvent(FollowService follow, string appId, string typeName, ClaimsPrincipal user)
    {
        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), user, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 1, cts);
        cts.Cancel();
        return events.Single().MaskedPayload!;
    }

    private static async Task PublishAsync(
        PublishService publish, string appId, string typeName, string payload, ClaimsPrincipal? user = null)
    {
        var result = await publish.PublishAsync(typeName, new PublishEventRequest(appId, 1, payload, null, null, null), user ?? TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
    }

    private static async Task RequestErasureAsync(
        PublishService publish, EventStoreContext db, SchemaRegistryService registry, UpcastChain upcastChain, ErasureKeyService erasureKeyService,
        string appId, string targetEntityId)
    {
        await PublishAsync(publish, appId, EntityErasureRequestedEventType.Name,
            $$"""{ "TargetEntityId": "{{targetEntityId}}" }""", TestClaimsPrincipal.With("erasure:request"));
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);
    }

    public static async Task AClassifiedFieldIsStoredAsCiphertextNeverThePlaintext(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "erasure-demo-1";
        const string typeName = "PatientRecordedErasure1";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "PatientId": { "type": "string" },
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED",
                        "regulatoryClassification": "PHI" } }
                  }, "required": ["PatientId", "Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.PatientId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await PublishAsync(publish, appId, typeName, """{ "PatientId": "pat-1", "Diagnosis": "Hypertension" }""");

        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == typeName.ToLowerInvariant());
        var storedDiagnosis = JsonNode.Parse(stored.Payload)!["Diagnosis"]!.GetValue<string>();

        Assert.AreNotEqual("Hypertension", storedDiagnosis, "a regulatoryClassification-tagged field must never be persisted as plaintext");
        Assert.IsFalse(storedDiagnosis.Contains("Hypertension"), "the real value must not appear anywhere in the stored ciphertext string");
        Convert.FromBase64String(storedDiagnosis); // throws if this isn't even well-formed base64 ciphertext
    }

    public static async Task AClaimHolderSeesTheRealDecryptedValueAndANonHolderStillSeesMaskedUnaffectedByEncryption(
        SchemaRegistryService registry, PublishService publish, FollowService follow, EventStoreContext db, UpcastChain upcastChain)
    {
        const string appId = "erasure-demo-2";
        const string typeName = "PatientRecordedErasure2";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "PatientId": { "type": "string" },
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED",
                        "regulatoryClassification": "PHI" } }
                  }, "required": ["PatientId", "Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.PatientId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await PublishAsync(publish, appId, typeName, """{ "PatientId": "pat-2", "Diagnosis": "Hypertension" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain); // resolves StoredEvent.EntityId -- required before a decrypt lookup keyed on it means anything

        var withClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.AreEqual("Hypertension", withClaim["Diagnosis"]!["value"]!.GetValue<string>());

        var withoutClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);
        Assert.AreEqual("REDACTED", withoutClaim["Diagnosis"]!["masked"]!.GetValue<string>(),
            "ADR-057: claims-based masking is completely unaffected and unaware encryption exists");
    }

    public static async Task ErasingTheEntityDestroysTheKeyAndEveryFutureReadShowsErasedEvenForAClaimHolder(
        SchemaRegistryService registry, PublishService publish, FollowService follow, EventStoreContext db, UpcastChain upcastChain, ErasureKeyService erasureKeyService)
    {
        const string appId = "erasure-demo-3";
        const string typeName = "PatientRecordedErasure3";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "PatientId": { "type": "string" },
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED",
                        "regulatoryClassification": "PHI" } }
                  }, "required": ["PatientId", "Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.PatientId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await PublishAsync(publish, appId, typeName, """{ "PatientId": "pat-3", "Diagnosis": "Hypertension" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var beforeErasure = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.AreEqual("Hypertension", beforeErasure["Diagnosis"]!["value"]!.GetValue<string>());

        await RequestErasureAsync(publish, db, registry, upcastChain, erasureKeyService, appId, $"{appId}:{typeName.ToLowerInvariant()}:pat-3");

        var afterErasureWithClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.IsTrue(afterErasureWithClaim["Diagnosis"]!["erased"]!.GetValue<bool>(),
            "ADR-057: erased means no one can ever see it again, including someone who holds every claim");
        Assert.IsNull(afterErasureWithClaim["Diagnosis"]!["value"]);

        var afterErasureWithoutClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);
        Assert.AreEqual("REDACTED", afterErasureWithoutClaim["Diagnosis"]!["masked"]!.GetValue<string>(),
            "erasure must not change the non-claim-holder's masked view at all -- it never depended on the DEK");
    }

    public static async Task ErasureNeverRewritesTheEventLogTheChainHashSurvivesUnchanged(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, UpcastChain upcastChain, ErasureKeyService erasureKeyService)
    {
        const string appId = "erasure-demo-4";
        const string typeName = "PatientRecordedErasure4";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "PatientId": { "type": "string" },
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "regulatoryClassification": "PHI" } }
                  }, "required": ["PatientId", "Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.PatientId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await PublishAsync(publish, appId, typeName, """{ "PatientId": "pat-4", "Diagnosis": "Hypertension" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var before = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == typeName.ToLowerInvariant());

        await RequestErasureAsync(publish, db, registry, upcastChain, erasureKeyService, appId, $"{appId}:{typeName.ToLowerInvariant()}:pat-4");

        var after = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == before.EventId);
        Assert.AreEqual(before.Payload, after.Payload, "erasure destroys the DEK elsewhere -- it must never rewrite the stored ciphertext itself");
        Assert.AreEqual(before.PayloadHash, after.PayloadHash);
        Assert.AreEqual(before.ChainHash, after.ChainHash, "README.md's 'never lose or corrupt data' -- the hash chain must survive erasure unbroken");
    }

    public static async Task AnErasureScopePointingAtADifferentEntityErasesThatEntitysKeyNotTheEventsOwnEntity(
        SchemaRegistryService registry, PublishService publish, FollowService follow, EventStoreContext db, UpcastChain upcastChain, ErasureKeyService erasureKeyService)
    {
        const string appId = "erasure-demo-5";
        const string typeName = "ClaimSubmittedErasure5";
        var policyHolderEntityId = $"{appId}:policyholder:ph-5";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "ClaimId": { "type": "string" },
                    "PolicyHolderEntityId": { "type": "string" },
                    "PolicyHolderSsn": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "regulatoryClassification": "PII",
                        "erasureScope": "$.PolicyHolderEntityId" } }
                  }, "required": ["ClaimId", "PolicyHolderEntityId", "PolicyHolderSsn"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.ClaimId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await PublishAsync(publish, appId, typeName,
            $$"""{ "ClaimId": "claim-5", "PolicyHolderEntityId": "{{policyHolderEntityId}}", "PolicyHolderSsn": "123-45-6789" }""");
        await RouterWorker.RunOnceAsync(db, registry, upcastChain);

        var beforeErasure = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.AreEqual("123-45-6789", beforeErasure["PolicyHolderSsn"]!["value"]!.GetValue<string>(),
            "encrypt and decrypt must resolve the SAME cross-entity erasureScope pointer to find the same DEK");

        // Erases the POLICY HOLDER's key, never the claim event's own entity --
        // the claim entity itself is untouched, only this one classified field's
        // scoped ownership is what determines what gets destroyed.
        await RequestErasureAsync(publish, db, registry, upcastChain, erasureKeyService, appId, policyHolderEntityId);

        var afterErasure = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.IsTrue(afterErasure["PolicyHolderSsn"]!["erased"]!.GetValue<bool>(),
            "erasing the scoped-to entity must erase the field even though the claim event's OWN entity was never targeted");
    }
}
