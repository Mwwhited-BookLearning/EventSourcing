using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Upcast Materialization + Downcast" (docs/08-build-
// plan.md)'s ADR-028 half -- read-time-only, never materialized, no safe
// pass-through fallback (a missing hop is a hard 400 at CONNECT time, not a
// best-effort guess). Exercised through FollowService the same way
// UpcastingScenarioAssertions already covers the upcast half.
internal static class DowncastScenarioAssertions
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(10);

    private static async Task<FollowedEvent> ConnectReplayAndCollectOne(FollowService follow, string appId, string typeName, int? asOfSchemaVersion)
    {
        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0, AsOfSchemaVersion: asOfSchemaVersion),
            TestClaimsPrincipal.None, cts.Token);
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

    public static async Task ARequestForAGenuinelyOlderVersionReturnsTheOldShape(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "downcast-demo-1";
        const string typeName = "OrderDowncastSingleHop";

        var v1Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v1Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, 'Unknown' as Status",
            DowncastToPrevious: "event.Amount as Amount"));

        // Published natively AT v2 -- there's nothing to upcast; the caller
        // is asking to see it AS IF it were still v1.
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 2, """{ "Amount": 100, "Status": "Paid" }""", null, null), TestClaimsPrincipal.None);

        var followed = await ConnectReplayAndCollectOne(follow, appId, typeName, asOfSchemaVersion: 1);

        Assert.AreEqual(2, followed.Event.SchemaVersion, "the stored row itself is untouched -- only the served payload is downcasted");
        Assert.AreEqual(100L, (long)followed.MaskedPayload!["Amount"]!);
        Assert.IsNull(followed.MaskedPayload!["Status"], "Status doesn't exist in the requested v1 shape");
    }

    public static async Task AVersionWithNoDowncastToPreviousRegisteredFailsTheRequestRatherThanGuessing(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "downcast-demo-2";
        const string typeName = "OrderDowncastMissingHop";

        var v1Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v1Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }""";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null,
            UpcastFromPrevious: "event.Amount as Amount, 'Unknown' as Status",
            DowncastToPrevious: null)); // no downcast mapping registered for this hop

        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 2, """{ "Amount": 100, "Status": "Paid" }""", null, null), TestClaimsPrincipal.None);

        var result = await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0, AsOfSchemaVersion: 1),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<FollowResult.ValidationFailed>(result);
    }
}
