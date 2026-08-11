using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Hardening & Evolution" (docs/08-build-plan.md)'s
// event-upcasting sub-part (ADR-018), exercised through FollowService the
// same way FollowScenarioAssertions already covers every other Follow
// behavior -- a mode=replay burst spanning a registered upcaster's version
// gap must present every event in the current (active) schema's shape.
internal static class UpcastingScenarioAssertions
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(10);

    private static async Task<FollowedEvent> ConnectReplayAndCollectOne(FollowService follow, string appId, string typeName)
    {
        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);

        var moveNext = enumerator.MoveNextAsync().AsTask();
        var winner = await Task.WhenAny(moveNext, Task.Delay(PerItemTimeout, cts.Token));
        if (winner != moveNext)
        {
            cts.Cancel();
            Assert.Fail("Timed out waiting for the followed event");
        }
        Assert.IsTrue(await moveNext, "stream ended with no event");
        var result = enumerator.Current;
        cts.Cancel();
        return result;
    }

    public static async Task AV1StoredEventIsPresentedUpcastedToTheActiveV2ShapeOnReplay(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "upcast-demo-1";
        const string typeName = "OrderUpcastSingleHop";

        var v1Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v1Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var created = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 100 }""", null, null), TestClaimsPrincipal.None);

        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, 'Unknown' as Status", DowncastToPrevious: "Amount"));

        var followed = await ConnectReplayAndCollectOne(follow, appId, typeName);

        Assert.AreEqual(created.CorrelationId, followed.Event.EventId);
        Assert.AreEqual(1, followed.Event.SchemaVersion, "the stored row itself is untouched -- only the served payload is upcasted");
        Assert.AreEqual(100L, (long)followed.MaskedPayload!["Amount"]!);
        Assert.AreEqual("Unknown", (string)followed.MaskedPayload!["Status"]!);
    }

    public static async Task AV1StoredEventSpanningTwoVersionHopsAppliesBothInOrder(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "upcast-demo-2";
        const string typeName = "OrderUpcastMultiHop";

        var v1Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v1Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var created = (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 50 }""", null, null), TestClaimsPrincipal.None);

        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, 'Unknown' as Status", DowncastToPrevious: "Amount"));

        var v3Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" }, "Currency": { "type": "string" } }, "required": ["Amount", "Status", "Currency"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v3Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, event.Status as Status, 'USD' as Currency", DowncastToPrevious: "Amount, Status"));

        var followed = await ConnectReplayAndCollectOne(follow, appId, typeName);

        Assert.AreEqual(created.CorrelationId, followed.Event.EventId);
        Assert.AreEqual(50L, (long)followed.MaskedPayload!["Amount"]!);
        Assert.AreEqual("Unknown", (string)followed.MaskedPayload!["Status"]!);
        Assert.AreEqual("USD", (string)followed.MaskedPayload!["Currency"]!);
    }
}
