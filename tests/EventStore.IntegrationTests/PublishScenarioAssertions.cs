using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Publish API" (docs/08-build-plan.md), mirroring
// docs/features/publish-event.md and the publish-side scenarios in
// docs/features/event-chains.md -- translated to this item's own
// pre-ADR-023 status codes (201/409/400/404), per that build-plan item's
// own explicit "Clarification" note. The querying-side event-chains.md
// scenarios (ancestors/descendants/parents/children reads) belong to the
// later "Lineage API" item, not exercised here.
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

        AssertCreated(result, out var created);
        Assert.AreEqual(1, created.SchemaVersion);
        Assert.IsGreaterThan(0, created.SequenceNumber);
    }

    public static async Task PublishingAnEventMissingARequiredFieldIsRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-2");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-2", SchemaVersion: 1, Payload: """{ "Amount": 150.00 }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.ValidationFailed>(result);
    }

    public static async Task PublishingAnEventWithAWrongShapedFieldIsRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-3");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-3", SchemaVersion: 1, Payload: """{ "Amount": "not-a-number", "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.ValidationFailed>(result);
    }

    public static async Task PublishingAgainstAnUnregisteredEventTypeIsRejected(PublishService publish)
    {
        var result = await publish.PublishAsync("NonExistentType", new PublishEventRequest(
            AppId: "publish-demo-4", SchemaVersion: 1, Payload: """{ "foo": "bar" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.UnregisteredEventType>(result);
    }

    public static async Task PublishingValidatesAgainstTheDeclaredVersionNotWhicheverIsActive(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-5";
        await RegisterOrderPlacedV1(registry, appId);
        var v2Schema = """
            { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["Amount", "Status", "Currency"] }
            """;
        // A real upcastFromPrevious mapping (not null) -- ADR-020's publish-time
        // compatibility check now runs the v1-shaped payload below through this
        // mapping and validates the result against v2, so v2 must actually be
        // reachable from v1 or this scenario's own "still validates against the
        // declared version" assertion would be masked by an unrelated dead-letter.
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, event.Status as Status, 'USD' as Currency", DowncastToPrevious: "Amount, Status"));
        // v2 is now active and requires "Currency" -- a v1-shaped payload declaring
        // schemaVersion 1 explicitly must still validate against v1, not "whichever is active".

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertCreated(result, out var created);
        Assert.AreEqual(1, created.SchemaVersion);
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
        AssertCreated(first, out var firstCreated);

        var second = await publish.PublishAsync("OrderPlaced", request, TestClaimsPrincipal.None, CancellationToken.None);
        Assert.IsInstanceOfType<PublishResult.IdempotentReplay>(second);
        var replay = (PublishResult.IdempotentReplay)second;
        Assert.AreEqual(firstCreated.EventId, replay.EventId);
        Assert.AreEqual(firstCreated.SequenceNumber, replay.SequenceNumber);
    }

    public static async Task RetryingWithSameEventIdButDifferentContentIsAConflict(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-7";
        await RegisterOrderPlacedV1(registry, appId);
        var eventId = Guid.NewGuid();

        var first = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: eventId), TestClaimsPrincipal.None, CancellationToken.None);
        AssertCreated(first, out _);

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
        AssertCreated(first, out var firstCreated);
        AssertCreated(second, out var secondCreated);

        Assert.AreNotEqual(firstCreated.EventId, secondCreated.EventId);
    }

    public static async Task PublishingAnOriginEventHasNoParents(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-9";
        await RegisterOrderPlacedV1(registry, appId);

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertCreated(result, out _);
    }

    public static async Task PublishingAChildEventParentedOffAPriorEventSucceeds(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-10";
        await RegisterOrderPlacedV1(registry, appId);

        var parent = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);
        AssertCreated(parent, out var parentCreated);

        var child = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Shipped" }""",
            ParentEventIds: [parentCreated.EventId], EventId: null), TestClaimsPrincipal.None);
        AssertCreated(child, out _);
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

        AssertCreated(result, out _);
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
        AssertCreated(withClaim, out _);
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
        AssertCreated(withPublishClaim, out _);
    }

    // ADR-020 -- a declared schemaVersion behind the active one is run through
    // UpcastChain right now, against this real payload, as a live compatibility
    // check. A real upcastFromPrevious mapping that actually reaches the active
    // version's shape must let the original publish through unchanged.
    public static async Task PublishingALaggingVersionWithACompatibleUpcastStoresTheOriginalPayloadUnchanged(
        SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-15";
        await RegisterOrderPlacedV1(registry, appId);
        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["Amount", "Status", "Currency"] }""";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, event.Status as Status, 'USD' as Currency", DowncastToPrevious: "Amount, Status"));

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertCreated(result, out var created);
        Assert.AreEqual(1, created.SchemaVersion, "the event is stored exactly as declared -- Payload is never transformed before storage");
        Assert.AreEqual("orderplaced", created.EventType);
    }

    // ADR-020's dead-letter path -- a lagging publish whose upcast hop fails
    // (here: no upcastFromPrevious mapping at all onto a version that added a
    // new required field, so the passed-through payload can never satisfy v2)
    // is not rejected outright and is not silently stored as if nothing were
    // wrong -- it's stored as the reserved EventUpcastFailed type instead.
    public static async Task PublishingALaggingVersionWithAFailingUpcastStoresEventUpcastFailedInstead(
        SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-16";
        await RegisterOrderPlacedV1(registry, appId);
        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["Amount", "Status", "Currency"] }""";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null), TestClaimsPrincipal.None);

        AssertCreated(result, out var created);
        Assert.AreEqual(PublishService.EventUpcastFailedEventType, created.EventType);
    }

    private static void AssertCreated(PublishResult result, out PublishResult.Created created)
    {
        if (result is PublishResult.ValidationFailed vf)
            Assert.Fail("Unexpected validation errors: " + string.Join(" | ", vf.Errors));
        Assert.IsInstanceOfType<PublishResult.Created>(result);
        created = (PublishResult.Created)result;
    }
}
