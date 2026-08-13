using EventStore.Domain.EventLog;
using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Follow API + Filter Pushdown" (docs/08-build-plan.md),
// covering that item's exit criteria verbatim. FollowService/EventTailReader are
// exercised directly (as every other API in this build stage is), not through a
// real HTTP/SSE round-trip -- FollowEndpoints.cs is a thin, untested wrapper
// consistent with every other *Endpoints.cs in this repo.
internal static class FollowScenarioAssertions
{
    private const string SimpleSchema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Name": { "type": "string" } }, "required": ["Amount"] }""";
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(10);

    private static Task RegisterType(SchemaRegistryService registry, string appId, string typeName, params (string JsonPath, string DataType)[] filterableFields) =>
        registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema,
            FilterableFields: filterableFields.Select(f => new FilterableFieldRequest(f.JsonPath, f.DataType, IsIndexed: false)).ToList(),
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    private static async Task<PublishResult.Accepted> Publish(PublishService publish, string appId, string typeName, decimal amount) =>
        (PublishResult.Accepted)await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, $$"""{ "Amount": {{amount}}, "Name": "n" }""", null, null), TestClaimsPrincipal.None);

    // Pulls exactly `count` items from a live poll loop, failing fast (rather than
    // hanging forever) if the loop doesn't produce one within PerItemTimeout --
    // guards against this test class itself wedging CI on a real bug. Takes an
    // already-obtained enumerator (not the IAsyncEnumerable itself) so a caller
    // can pull history and then, later, the tail from the SAME live loop --
    // re-enumerating the IAsyncEnumerable would restart EventTailReader's poll
    // loop from its original lastSeen, not continue it.
    private static async Task<List<StoredEvent>> Collect(IAsyncEnumerator<FollowedEvent> enumerator, int count, CancellationTokenSource cts) =>
        (await CollectFollowed(enumerator, count, cts)).Select(f => f.Event).ToList();

    private static async Task<List<FollowedEvent>> CollectFollowed(IAsyncEnumerator<FollowedEvent> enumerator, int count, CancellationTokenSource cts)
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

    public static async Task ConnectingWithNoFilterInReplayModeStreamsEveryEventOfTheType(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-1";
        const string typeName = "OrderPlacedNoFilter";
        await RegisterType(registry, appId, typeName);
        await Publish(publish, appId, typeName, 10);
        await Publish(publish, appId, typeName, 20);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 2, cts);
        cts.Cancel();

        Assert.HasCount(2, events);
    }

    public static async Task FilterOnANumberFieldStreamsOnlyMatchingEventsIncludingCombinedConditions(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-2";
        const string typeName = "OrderPlacedFiltered";
        await RegisterType(registry, appId, typeName, ("$.Amount", "Number"));
        await Publish(publish, appId, typeName, 50);
        var e150 = await Publish(publish, appId, typeName, 150);
        var e250 = await Publish(publish, appId, typeName, 250);

        using (var cts = new CancellationTokenSource())
        {
            var connected = (FollowResult.Connected)await follow.ConnectAsync(
                typeName, new FollowRequest(appId, Filter: "Amount gt 100", Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
            await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
            var events = await Collect(enumerator, 2, cts);
            cts.Cancel();
            CollectionAssert.AreEquivalent(new[] { e150.CorrelationId, e250.CorrelationId }, events.Select(e => e.EventId).ToArray());
        }

        using (var cts = new CancellationTokenSource())
        {
            var connected = (FollowResult.Connected)await follow.ConnectAsync(
                typeName, new FollowRequest(appId, Filter: "Amount gt 100 and Amount lt 200", Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
            await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
            var events = await Collect(enumerator, 1, cts);
            cts.Cancel();
            Assert.AreEqual(e150.CorrelationId, events.Single().EventId);
        }
    }

    public static async Task FilterReferencingAnUndeclaredFieldIsRejectedAtParseTimeBeforeAnySqlRuns(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-3";
        const string typeName = "OrderPlacedUndeclaredFilter";
        await RegisterType(registry, appId, typeName); // no FilterableFields registered at all
        await Publish(publish, appId, typeName, 999);

        var result = await follow.ConnectAsync(typeName, new FollowRequest(appId, Filter: "Amount gt 1", Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<FollowResult.ValidationFailed>(result);
    }

    public static async Task ModeReplayWithNoFromSequenceNumberDeliversHistoryThenTailsNewEventsWithNoGapOrDuplicate(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-4";
        const string typeName = "OrderPlacedReplayThenTail";
        await RegisterType(registry, appId, typeName);
        var e1 = await Publish(publish, appId, typeName, 1);
        var e2 = await Publish(publish, appId, typeName, 2);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: null), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);

        var history = await Collect(enumerator, 2, cts);
        CollectionAssert.AreEqual(new[] { e1.CorrelationId, e2.CorrelationId }, history.Select(e => e.EventId).ToArray());

        var e3 = await Publish(publish, appId, typeName, 3);
        var tailed = await Collect(enumerator, 1, cts);
        cts.Cancel();

        Assert.AreEqual(e3.CorrelationId, tailed.Single().EventId);
        Assert.AreEqual(e2.SequenceNumber + 1, tailed.Single().SequenceNumber, "expected no gap or duplicate between history and the tail");
    }

    public static async Task SupplyingFromSequenceNumberOnlyReplaysEventsAfterThatSequenceNumber(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-5";
        const string typeName = "OrderPlacedFromSequence";
        await RegisterType(registry, appId, typeName);
        var e1 = await Publish(publish, appId, typeName, 1);
        var e2 = await Publish(publish, appId, typeName, 2);
        var e3 = await Publish(publish, appId, typeName, 3);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: e1.SequenceNumber), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 2, cts);
        cts.Cancel();

        CollectionAssert.AreEqual(new[] { e2.CorrelationId, e3.CorrelationId }, events.Select(e => e.EventId).ToArray());
    }

    public static async Task ModeReplayCombinedWithFilterReplaysOnlyMatchingHistory(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-6";
        const string typeName = "OrderPlacedReplayFiltered";
        await RegisterType(registry, appId, typeName, ("$.Amount", "Number"));
        await Publish(publish, appId, typeName, 50);
        var e150 = await Publish(publish, appId, typeName, 150);
        var e250 = await Publish(publish, appId, typeName, 250);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: "Amount gt 100", Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 2, cts);
        cts.Cancel();

        CollectionAssert.AreEquivalent(new[] { e150.CorrelationId, e250.CorrelationId }, events.Select(e => e.EventId).ToArray());
    }

    public static async Task TheDefaultModeTailNeverDeliversPreExistingEvents(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-7";
        const string typeName = "OrderPlacedDefaultTail";
        await RegisterType(registry, appId, typeName);
        await Publish(publish, appId, typeName, 1); // pre-existing, must never appear

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: null, FromSequenceNumber: null), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);

        var e2 = await Publish(publish, appId, typeName, 2);
        var events = await Collect(enumerator, 1, cts);
        cts.Cancel();

        Assert.AreEqual(e2.CorrelationId, events.Single().EventId);
    }

    // Direct regression coverage for a real bug: EventTailReader.TailAsync's
    // idle-poll Task.Delay(pollInterval, ct) let a real client disconnect
    // (cancelling ct mid-await, the exact same thing closing a browser tab
    // does to a live GraphQL Subscription) propagate as an unhandled
    // OperationCanceledException/TaskCanceledException all the way through
    // FollowSubscriptionTypeModule's own pass-through wrapper and into
    // HotChocolate's subscription executor, instead of ending the stream
    // cleanly. No events are published for this type -- MoveNextAsync's
    // first call finds nothing and parks inside that exact Task.Delay,
    // the real suspension point a disconnect cancels mid-await; cancelling
    // before it has genuinely reached that await (or before the loop even
    // starts) would prove nothing about this bug.
    public static async Task DisconnectingMidTailEndsTheStreamGracefullyRatherThanThrowing(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-graceful-cancel";
        const string typeName = "OrderPlacedGracefulCancel";
        await RegisterType(registry, appId, typeName);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: null, FromSequenceNumber: null), TestClaimsPrincipal.None, cts.Token);
        var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        cts.Cancel();

        var hasNext = await moveNextTask; // must complete cleanly, not throw, despite cancellation firing mid-await
        Assert.IsFalse(hasNext, "cancelling the follow token mid-poll must end the stream cleanly, not throw");

        await enumerator.DisposeAsync();
    }

    public static async Task SupplyingFromSequenceNumberWithoutModeReplayIsRejected(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-8";
        const string typeName = "OrderPlacedBadFromSequence";
        await RegisterType(registry, appId, typeName);
        await Publish(publish, appId, typeName, 1);

        var result = await follow.ConnectAsync(typeName, new FollowRequest(appId, Filter: null, Mode: null, FromSequenceNumber: 1), TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<FollowResult.ValidationFailed>(result);
    }

    public static async Task ConnectingToAnUnregisteredEventTypeIsRejected(FollowService follow)
    {
        var result = await follow.ConnectAsync("NoSuchType", new FollowRequest("no-such-app", null, null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<FollowResult.UnregisteredEventType>(result);
    }

    public static async Task ConnectingWithoutTheRequiredReadClaimIsRejectedWith403(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-9";
        const string typeName = "PatientAdmitted";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Read", "clearance:phi")],
            UpcastFromPrevious: null, DowncastToPrevious: null));
        await Publish(publish, appId, typeName, 1);

        var withoutClaim = await follow.ConnectAsync(typeName, new FollowRequest(appId, null, "Replay", 0), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<FollowResult.Forbidden>(withoutClaim);

        var withClaim = await follow.ConnectAsync(typeName, new FollowRequest(appId, null, "Replay", 0), TestClaimsPrincipal.With("clearance:phi"));
        Assert.IsInstanceOfType<FollowResult.Connected>(withClaim);
    }

    public static async Task ARestrictedParentsIdIsOmittedFromParentEventIdsWithoutBlockingTheEventItself(SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "follow-demo-10";
        const string parentTypeName = "PaymentReceived";
        const string childTypeName = "OrderShipped";
        await registry.RegisterAsync(parentTypeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: SimpleSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Read", "clearance:phi")],
            UpcastFromPrevious: null, DowncastToPrevious: null));
        await RegisterType(registry, appId, childTypeName); // no RequiredClaims -- visible to anyone with events:follow

        var parent = await Publish(publish, appId, parentTypeName, 1);
        var child = (PublishResult.Accepted)await publish.PublishAsync(childTypeName, new PublishEventRequest(
            appId, 1, $$"""{ "Amount": 1, "Name": "n" }""", [parent.CorrelationId], null), TestClaimsPrincipal.None);

        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            childTypeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None);
        await using var enumerator = connected.Events.GetAsyncEnumerator(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var followed = (await CollectFollowed(enumerator, 1, cts)).Single();

        Assert.AreEqual(child.CorrelationId, followed.Event.EventId, "the event itself must still stream despite the restricted parent");
        Assert.IsEmpty(followed.VisibleParentEventIds, "a parent the caller lacks the Read claim for must be omitted, not just redacted");
    }
}
