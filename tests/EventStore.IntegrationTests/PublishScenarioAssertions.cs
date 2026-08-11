using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Publish API" (docs/08-build-plan.md), mirroring
// docs/features/publish-event.md's ADR-023 (persist-everything) contract --
// rewritten for "Entity-Centric Core Rebuild" (build-plan item 12): every
// syntactically-parseable, authorized, non-conflicting publish now returns
// PublishResult.Accepted (202) regardless of schema validity; only an
// unregistered event type, a Strict-mode unresolved parent, an eventId
// content conflict, and missing scope/claims remain real rejections. What
// used to be PublishResult.ValidationFailed scenarios here are now
// PublishResult.Accepted scenarios instead -- whether the payload actually
// conforms to its schema becomes an ASYNC, advisory SchemaStatus the Router
// sets afterward, covered by EntityScenarioAssertions.cs (which has the
// db/router access this file deliberately doesn't).
internal static class PublishScenarioAssertions
{
    private const string OrderPlacedSchemaV1 = """
        { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }
        """;

    private static async Task RegisterOrderPlacedV1(SchemaRegistryService registry, string appId = "demo") =>
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: OrderPlacedSchemaV1, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task PublishingAValidEventSucceeds(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-1");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-1", SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertAccepted(result, out var accepted);
        Assert.AreEqual("received", accepted.Status, "the Router hasn't run yet at this synchronous point");
        Assert.IsGreaterThan(0, accepted.SequenceNumber);
    }

    public static async Task PublishingAnEventMissingARequiredFieldIsPersistedNotRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-2");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-2", SchemaVersion: 1, Payload: """{ "Amount": 150.00 }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertAccepted(result, out var accepted);
        Assert.IsNull(accepted.SchemaStatus, "not yet evaluated by the Router at this synchronous point");
    }

    public static async Task PublishingAnEventWithAWrongShapedFieldIsPersistedNotRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-3");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-3", SchemaVersion: 1, Payload: """{ "Amount": "not-a-number", "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertAccepted(result, out _);
    }

    public static async Task PublishingAgainstAnUnregisteredEventTypeIsRejected(PublishService publish)
    {
        var result = await publish.PublishAsync("NonExistentType", new PublishEventRequest(
            AppId: "publish-demo-4", SchemaVersion: 1, Payload: """{ "foo": "bar" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.UnregisteredEventType>(result);
    }

    public static async Task RetryingWithSameEventIdAndIdenticalContentReplaysWithNoNewWrite(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-6";
        await RegisterOrderPlacedV1(registry, appId);
        var eventId = Guid.NewGuid();
        var request = new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: eventId);

        var first = await publish.PublishAsync("OrderPlaced", request, TestClaimsPrincipal.None, CancellationToken.None);
        AssertAccepted(first, out var firstAccepted);

        var second = await publish.PublishAsync("OrderPlaced", request, TestClaimsPrincipal.None, CancellationToken.None);
        AssertAccepted(second, out var secondAccepted);
        Assert.AreEqual(firstAccepted.CorrelationId, secondAccepted.CorrelationId);
        Assert.AreEqual(firstAccepted.SequenceNumber, secondAccepted.SequenceNumber);
    }

    public static async Task RetryingWithSameEventIdButDifferentContentIsAConflict(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-7";
        await RegisterOrderPlacedV1(registry, appId);
        var eventId = Guid.NewGuid();

        var first = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: eventId), TestClaimsPrincipal.None, CancellationToken.None);
        AssertAccepted(first, out _);

        var second = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 999.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: eventId), TestClaimsPrincipal.None, CancellationToken.None);

        Assert.IsInstanceOfType<PublishResult.Conflict>(second);
    }

    public static async Task PublishingWithoutEventIdGeneratesAFreshOneEachTime(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-8";
        await RegisterOrderPlacedV1(registry, appId);
        var request = new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null);

        var first = await publish.PublishAsync("OrderPlaced", request, TestClaimsPrincipal.None, CancellationToken.None);
        var second = await publish.PublishAsync("OrderPlaced", request, TestClaimsPrincipal.None, CancellationToken.None);
        AssertAccepted(first, out var firstAccepted);
        AssertAccepted(second, out var secondAccepted);

        Assert.AreNotEqual(firstAccepted.CorrelationId, secondAccepted.CorrelationId);
    }

    public static async Task PublishingAnOriginEventHasNoParents(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-9";
        await RegisterOrderPlacedV1(registry, appId);

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertAccepted(result, out _);
    }

    public static async Task PublishingAChildEventParentedOffAPriorEventSucceeds(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-10";
        await RegisterOrderPlacedV1(registry, appId);

        var parent = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        AssertAccepted(parent, out var parentAccepted);

        var child = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Shipped" }""",
            ParentEventIds: [parentAccepted.CorrelationId], EventId: null), TestClaimsPrincipal.None);
        AssertAccepted(child, out _);
    }

    public static async Task StrictParentValidationRejectsAnUnresolvedParent(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-11";
        await registry.RegisterAsync("OrderShipped", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Carrier": { "type": "string" } }, "required": ["Carrier"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Strict", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var result = await publish.PublishAsync("OrderShipped", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Carrier": "UPS" }""",
            ParentEventIds: [Guid.Empty], EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.UnresolvedParent>(result);
    }

    public static async Task PermissiveParentValidationAcceptsADanglingParentReference(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-12";
        await registry.RegisterAsync("OrderShipped", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Carrier": { "type": "string" } }, "required": ["Carrier"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var result = await publish.PublishAsync("OrderShipped", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Carrier": "UPS" }""",
            ParentEventIds: [Guid.Empty], EventId: null), TestClaimsPrincipal.None);

        AssertAccepted(result, out _);
    }

    public static async Task PublishingAClaimGatedTypeWithoutTheClaimIsRejectedWith403AndWithItSucceeds(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-13";
        await registry.RegisterAsync("PatientAdmitted", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Publish", "clearance:phi")],
            UpcastFromPrevious: null, DowncastToPrevious: null));

        var withoutClaim = await publish.PublishAsync("PatientAdmitted", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 1 }""", ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Forbidden>(withoutClaim);

        var withClaim = await publish.PublishAsync("PatientAdmitted", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 1 }""", ParentEventIds: null, EventId: null), TestClaimsPrincipal.With("clearance:phi"));
        AssertAccepted(withClaim, out _);
    }

    // Publish- and Read-direction claims for the same event type are independent
    // -- a caller with only one of the two must be gated on exactly that one.
    public static async Task PublishAndReadClaimsAreEnforcedFullyIndependentlyForTheSameType(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-14";
        await registry.RegisterAsync("LabResultRecorded", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims:
            [
                new RequiredClaimRequest("Publish", "role:lab-tech"),
                new RequiredClaimRequest("Read", "clearance:phi"),
            ],
            UpcastFromPrevious: null, DowncastToPrevious: null));

        // Holds the Read claim but not the Publish claim -- publish must still be rejected.
        var result = await publish.PublishAsync("LabResultRecorded", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 1 }""", ParentEventIds: null, EventId: null), TestClaimsPrincipal.With("clearance:phi"));
        Assert.IsInstanceOfType<PublishResult.Forbidden>(result);

        // Holds the Publish claim (and only that) -- publish must succeed.
        var withPublishClaim = await publish.PublishAsync("LabResultRecorded", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 1 }""", ParentEventIds: null, EventId: null), TestClaimsPrincipal.With("role:lab-tech"));
        AssertAccepted(withPublishClaim, out _);
    }

    private static void AssertAccepted(PublishResult result, out PublishResult.Accepted accepted)
    {
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        accepted = (PublishResult.Accepted)result;
    }
}
