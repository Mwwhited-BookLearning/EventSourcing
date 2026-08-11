using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Hardening & Evolution" (docs/08-build-plan.md)'s
// hash-chained tamper-evidence sub-part (ADR-019). Exercises PublishService's
// ChainHash computation and ChainVerificationService directly, the same way
// every other item in this build stage is tested through its service layer
// rather than a real HTTP round-trip.
internal static class HashChainScenarioAssertions
{
    private const string SimpleSchema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";

    private static Task RegisterType(SchemaRegistryService registry, string appId, string typeName) =>
        registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task PublishingEventsChainsEachEventsHashToItsPredecessor(
        SchemaRegistryService registry, PublishService publish, ChainVerificationService verifier)
    {
        const string appId = "hashchain-demo-1";
        const string typeName = "ChainedType1";
        await RegisterType(registry, appId, typeName);

        var e1 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var e3 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 3 }""", null, null), TestClaimsPrincipal.None);

        var result = await verifier.VerifyAsync(e3.SequenceNumber);

        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(result);
        var verified = (ChainVerificationResult.Verified)result;
        Assert.IsGreaterThanOrEqualTo(3, verified.EventCount);
    }

    public static async Task CorruptingAHistoricalPayloadIsDetectedAtExactlyThatSequenceNumberWithEverythingBeforeItVerifyingClean(
        SchemaRegistryService registry, PublishService publish, ChainVerificationService verifier, EventStoreContext db)
    {
        const string appId = "hashchain-demo-2";
        const string typeName = "ChainedType2";
        await RegisterType(registry, appId, typeName);

        var e1 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 10 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 20 }""", null, null), TestClaimsPrincipal.None);
        var e3 = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 30 }""", null, null), TestClaimsPrincipal.None);

        // Verifies clean before any corruption -- establishes the baseline this
        // scenario's actual assertion (below) is a change from.
        var clean = await verifier.VerifyAsync(e3.SequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(clean);

        // Test-only direct database edit -- bypasses PublishService entirely, so
        // PayloadHash is deliberately left stale, matching this item's own exit
        // criterion ("a direct database edit" to Payload alone).
        var row = await db.Events.SingleAsync(e => e.EventId == e2.CorrelationId);
        row.Payload = """{ "Amount": 999999 }""";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await verifier.VerifyAsync(e3.SequenceNumber);

        Assert.IsInstanceOfType<ChainVerificationResult.Tampered>(result);
        var tampered = (ChainVerificationResult.Tampered)result;
        Assert.AreEqual(e2.SequenceNumber, tampered.FirstDivergentSequenceNumber);

        // Everything strictly before the corrupted event still verifies clean.
        var beforeCorruption = await verifier.VerifyAsync(e1.SequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(beforeCorruption);
    }
}
