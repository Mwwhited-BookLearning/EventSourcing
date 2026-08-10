using EventStore.Domain.EventLog;
using EventStore.Erasure;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Digital Sign-Off for Regulated Actions (Step-Up
// Authentication)" (docs/08-build-plan.md, ADR-066). RFC 9470's actual
// challenge/response HTTP header is exercised separately by
// DigitalSignOffHttpSqliteTests (an HTTP-response-shaping concern, not this
// service's) -- everything checkable through PublishService/
// SchemaRegistryService/ChainVerificationService directly lives here,
// matching this project's dominant "exercise the service layer, not a
// bespoke test double" style.
internal static class DigitalSignOffScenarioAssertions
{
    private const string SignedSchema = """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""";

    private static Task<RegisterEventTypeResult> RegisterSignedType(
        SchemaRegistryService registry, string appId, string typeName, List<string> acrValues, int? maxAge) =>
        registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SignedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null,
            RequiredSignature: new RequiredSignatureRequest(acrValues, maxAge)));

    public static async Task RegisteringARequiredSignatureNamingNeitherAcrValuesNorMaxAgeIsRejected(SchemaRegistryService registry)
    {
        var result = await RegisterSignedType(registry, "signoff-demo-1", "BadSignoff1", [], null);
        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result,
            "a RequiredSignature enforcing nothing at all is always a request mistake, not a legitimate no-op");
    }

    public static async Task RegisteringARequiredSignatureWithANonPositiveMaxAgeIsRejected(SchemaRegistryService registry)
    {
        var result = await RegisterSignedType(registry, "signoff-demo-2", "BadSignoff2", [], 0);
        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result);
    }

    public static async Task RegisteringARequiredSignatureWithOnlyAcrValuesOrOnlyMaxAgeBothSucceed(SchemaRegistryService registry)
    {
        var acrOnly = await RegisterSignedType(registry, "signoff-demo-3", "GoodSignoffAcrOnly3", ["urn:eventstore:step-up"], null);
        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(acrOnly);

        var maxAgeOnly = await RegisterSignedType(registry, "signoff-demo-3", "GoodSignoffMaxAgeOnly3", [], 300);
        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(maxAgeOnly);
    }

    public static async Task APublishAgainstASignatureRequiredTypeFromACallerWithNoAcrClaimAtAllIsRejectedWithStepUpRequired(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "signoff-demo-4";
        const string typeName = "RequiresStepUp4";
        await RegisterSignedType(registry, appId, typeName, ["urn:eventstore:step-up"], null);

        var result = await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null, Meaning: "approved"), TestClaimsPrincipal.None);

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        CollectionAssert.AreEqual(new[] { "urn:eventstore:step-up" }, stepUp.AcrValues.ToArray());
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == appId), "no event may be persisted for a rejected step-up attempt");
    }

    public static async Task APublishWithTheWrongAcrValueIsRejectedEvenThoughAnAcrClaimIsPresent(
        SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "signoff-demo-5";
        const string typeName = "RequiresStepUp5";
        await RegisterSignedType(registry, appId, typeName, ["urn:eventstore:step-up"], null);

        var result = await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null, Meaning: "approved"),
            TestClaimsPrincipal.WithClaims(("acr", "urn:eventstore:some-other-level")));

        Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
    }

    public static async Task APublishWithAnAuthTimeOlderThanMaxAgeIsRejectedWithStepUpRequired(
        SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "signoff-demo-6";
        const string typeName = "RequiresStepUp6";
        await RegisterSignedType(registry, appId, typeName, [], 300); // 5 minutes

        var staleAuthTime = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var result = await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null, Meaning: "approved"),
            TestClaimsPrincipal.WithClaims(("auth_time", staleAuthTime)));

        var stepUp = Assert.IsInstanceOfType<PublishResult.StepUpRequired>(result);
        Assert.AreEqual(300, stepUp.MaxAge);
    }

    public static async Task APublishSatisfyingStepUpButOmittingMeaningIsRejectedAsAnIncompleteEnvelope(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "signoff-demo-7";
        const string typeName = "RequiresStepUp7";
        await RegisterSignedType(registry, appId, typeName, ["urn:eventstore:step-up"], null);

        var result = await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null), // Meaning omitted
            TestClaimsPrincipal.WithClaims(("acr", "urn:eventstore:step-up")));

        Assert.IsInstanceOfType<PublishResult.MissingSignatureMeaning>(result);
        Assert.IsFalse(await db.Events.AnyAsync(e => e.AppId == appId),
            "an incomplete signed envelope is never persisted with an advisory flag");
    }

    public static async Task ASuccessfulSignedPublishPopulatesAllFourSignatureFields(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "signoff-demo-8";
        const string typeName = "RequiresStepUp8";
        await RegisterSignedType(registry, appId, typeName, ["urn:eventstore:step-up"], 300);

        var recentAuthTime = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();
        var result = await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null, Meaning: "reviewed"),
            TestClaimsPrincipal.WithClaims(("sub", "signer-1"), ("acr", "urn:eventstore:step-up"), ("auth_time", recentAuthTime)));

        var accepted = Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);

        Assert.IsNotNull(stored.Signature);
        Assert.AreEqual("signer-1", stored.Signature!.SignerId);
        Assert.AreEqual("reviewed", stored.Signature.Meaning);
        Assert.AreEqual("urn:eventstore:step-up", stored.Signature.Acr);
        Assert.IsTrue(stored.Signature.SignedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.AreEqual("signer-1", stored.ActorId, "ADR-064: ActorId is the verified token subject for every publish, signed or not");
    }

    public static async Task AlteringAStoredSignaturesMeaningDirectlyInTheDatabaseIsDetectedByOrdinaryChainVerification(
        SchemaRegistryService registry, PublishService publish, ChainVerificationService verifier, EventStoreContext db)
    {
        const string appId = "signoff-demo-9";
        const string typeName = "RequiresStepUp9";
        await RegisterSignedType(registry, appId, typeName, ["urn:eventstore:step-up"], null);

        var e1 = Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1" }""", null, null, Meaning: "approved"),
            TestClaimsPrincipal.WithClaims(("acr", "urn:eventstore:step-up"))));
        var e2 = Assert.IsInstanceOfType<PublishResult.Accepted>(await publish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-2" }""", null, null, Meaning: "approved"),
            TestClaimsPrincipal.WithClaims(("acr", "urn:eventstore:step-up"))));

        var cleanResult = await verifier.VerifyAsync(e2.SequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(cleanResult, "must verify clean before any corruption -- the baseline this scenario changes");

        // A direct-database edit to Signature.Meaning alone, ChainHash left
        // untouched -- exactly the "corrupt the column, not the hash" shape
        // HashChainScenarioAssertions' own Payload-corruption test already
        // establishes for Payload itself. Signature is stored via a JSON
        // ValueConverter with no ValueComparer configured (JsonValueConverter.cs),
        // so EF's default reference-equality change detection never notices
        // an in-place mutation of the SAME Signature instance -- a NEW
        // instance has to be assigned for SaveChangesAsync to actually issue
        // an UPDATE, found only by running this exact scenario, not by
        // reading the mapping code back.
        var target = await db.Events.SingleAsync(e => e.EventId == e1.CorrelationId);
        target.Signature = new Signature
        {
            SignerId = target.Signature!.SignerId, SignedAt = target.Signature.SignedAt, Meaning = "TAMPERED", Acr = target.Signature.Acr,
        };
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var tamperedResult = await verifier.VerifyAsync(e2.SequenceNumber);
        var tampered = Assert.IsInstanceOfType<ChainVerificationResult.Tampered>(tamperedResult);
        Assert.AreEqual(e1.SequenceNumber, tampered.FirstDivergentSequenceNumber);
    }

    // `signedAndEncryptedPublish` must already be constructed with a real
    // PayloadEncryptor (ErasureTestSupport.CreateErasureStack, "GDPR/CCPA
    // Erasure"'s own test wiring) -- kept as a caller-supplied parameter,
    // like every other method here's own `publish`, rather than this file
    // constructing a second, provider-specific PublishService internally.
    public static async Task ErasingAnEntityWithASignedClassifiedEventLeavesSignerIdAndSignatureCompletelyIntact(
        SchemaRegistryService registry, PublishService signedAndEncryptedPublish, EventStoreContext db, ErasureKeyService erasureKeyService)
    {
        const string appId = "signoff-demo-10";
        const string typeName = "SignedAndClassified10";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "Id": { "type": "string" },
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "regulatoryClassification": "PHI" } }
                  }, "required": ["Id", "Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null,
            RequiredSignature: new RequiredSignatureRequest(["urn:eventstore:step-up"], null)));

        var result = await signedAndEncryptedPublish.PublishAsync(typeName,
            new PublishEventRequest(appId, 1, """{ "Id": "rec-1", "Diagnosis": "Hypertension" }""", null, null, Meaning: "approved"),
            TestClaimsPrincipal.WithClaims(("sub", "signer-1"), ("acr", "urn:eventstore:step-up")));
        var accepted = Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        var beforeErasure = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        Assert.IsNotNull(beforeErasure.Signature);

        var entityId = $"{appId}:{typeName.ToLowerInvariant()}:rec-1";
        await erasureKeyService.EraseAsync(entityId);

        var afterErasure = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        Assert.IsNotNull(afterErasure.Signature, "ADR-066: SignerId/Signature are a deliberate, reasoned exemption from ADR-057 erasure");
        Assert.AreEqual(beforeErasure.Signature!.SignerId, afterErasure.Signature!.SignerId);
        Assert.AreEqual(beforeErasure.Signature.Meaning, afterErasure.Signature.Meaning);
        Assert.AreEqual(beforeErasure.Signature.Acr, afterErasure.Signature.Acr);
        Assert.AreEqual(beforeErasure.Signature.SignedAt, afterErasure.Signature.SignedAt);
    }
}
