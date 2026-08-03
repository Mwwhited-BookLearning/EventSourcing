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
            ParentEventIds: null, EventId: null));

        AssertCreated(result, out var created);
        Assert.AreEqual(1, created.SchemaVersion);
        Assert.IsGreaterThan(0, created.SequenceNumber);
    }

    public static async Task PublishingAnEventMissingARequiredFieldIsRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-2");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-2", SchemaVersion: 1, Payload: """{ "Amount": 150.00 }""",
            ParentEventIds: null, EventId: null));

        Assert.IsInstanceOfType<PublishResult.ValidationFailed>(result);
    }

    public static async Task PublishingAnEventWithAWrongShapedFieldIsRejected(SchemaRegistryService registry, PublishService publish)
    {
        await RegisterOrderPlacedV1(registry, "publish-demo-3");

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: "publish-demo-3", SchemaVersion: 1, Payload: """{ "Amount": "not-a-number", "Status": "Paid" }""",
            ParentEventIds: null, EventId: null));

        Assert.IsInstanceOfType<PublishResult.ValidationFailed>(result);
    }

    public static async Task PublishingAgainstAnUnregisteredEventTypeIsRejected(PublishService publish)
    {
        var result = await publish.PublishAsync("NonExistentType", new PublishEventRequest(
            AppId: "publish-demo-4", SchemaVersion: 1, Payload: """{ "foo": "bar" }""",
            ParentEventIds: null, EventId: null));

        Assert.IsInstanceOfType<PublishResult.UnregisteredEventType>(result);
    }

    public static async Task PublishingValidatesAgainstTheDeclaredVersionNotWhicheverIsActive(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-5";
        await RegisterOrderPlacedV1(registry, appId);
        var v2Schema = """
            { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["Amount", "Status", "Currency"] }
            """;
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        // v2 is now active and requires "Currency" -- a v1-shaped payload declaring
        // schemaVersion 1 explicitly must still validate against v1, not "whichever is active".

        var result = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null));

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

        var first = await publish.PublishAsync("OrderPlaced", request, CancellationToken.None);
        AssertCreated(first, out var firstCreated);

        var second = await publish.PublishAsync("OrderPlaced", request, CancellationToken.None);
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
            ParentEventIds: null, EventId: eventId), CancellationToken.None);
        AssertCreated(first, out _);

        var second = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 999.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: eventId), CancellationToken.None);

        Assert.IsInstanceOfType<PublishResult.Conflict>(second);
    }

    public static async Task PublishingWithoutEventIdGeneratesAFreshOneEachTime(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-8";
        await RegisterOrderPlacedV1(registry, appId);
        var request = new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null);

        var first = await publish.PublishAsync("OrderPlaced", request, CancellationToken.None);
        var second = await publish.PublishAsync("OrderPlaced", request, CancellationToken.None);
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
            ParentEventIds: null, EventId: null));

        AssertCreated(result, out _);
    }

    public static async Task PublishingAChildEventParentedOffAPriorEventSucceeds(SchemaRegistryService registry, PublishService publish)
    {
        const string appId = "publish-demo-10";
        await RegisterOrderPlacedV1(registry, appId);

        var parent = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Paid" }""",
            ParentEventIds: null, EventId: null));
        AssertCreated(parent, out var parentCreated);

        var child = await publish.PublishAsync("OrderPlaced", new PublishEventRequest(
            AppId: appId, SchemaVersion: 1, Payload: """{ "Amount": 150.00, "Status": "Shipped" }""",
            ParentEventIds: [parentCreated.EventId], EventId: null));
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
            ParentEventIds: [Guid.Empty], EventId: null));

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
            ParentEventIds: [Guid.Empty], EventId: null));

        AssertCreated(result, out _);
    }

    private static void AssertCreated(PublishResult result, out PublishResult.Created created)
    {
        if (result is PublishResult.ValidationFailed vf)
            Assert.Fail("Unexpected validation errors: " + string.Join(" | ", vf.Errors));
        Assert.IsInstanceOfType<PublishResult.Created>(result);
        created = (PublishResult.Created)result;
    }
}
