using EventStore.Domain.SchemaRegistry;
using EventStore.ExpectedResponse;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Expected-Response Tracking" (docs/08-build-plan.md,
// ADR-094). Exercises ExpectedResponseWatcher.RunOnceAsync directly against
// a real provider-backed context, the same "drive the static entry point
// directly, bypass the leader lease" pattern RouterWorker/DerivationWorker/
// WebhookOutboxPump's own tests already establish.
internal static class ExpectedResponseScenarioAssertions
{
    private static async Task RegisterRequestTypeWithExpectedResponse(
        SchemaRegistryService registry, string appId, string requestType, string responseEventType, TimeSpan within)
    {
        await registry.RegisterAsync(requestType, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null,
            ExpectedResponse: new ExpectedResponseRequest(responseEventType, within)));
        await registry.RegisterAsync(responseEventType, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
    }

    public static async Task AnEventTypeWithNoExpectedResponseConfiguredGetsNoTrackerRowAndNoWatcherActivity(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-1";
        await registry.RegisterAsync("PlainRequest", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Id": { "type": "string" } }, "required": ["Id"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var e1 = (PublishResult.Accepted)await publish.PublishAsync("PlainRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);

        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);

        Assert.IsFalse(await db.ExpectedResponseTrackers.AnyAsync(t => t.RequestEventId == e1.CorrelationId), "an event type with no ExpectedResponse configured must never get a tracker row -- purely additive behavior");
    }

    public static async Task AnEventTypeWithExpectedResponseConfiguredGetsATrackerRowWithTheCorrectDeadline(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-2";
        var within = TimeSpan.FromMinutes(5);
        await RegisterRequestTypeWithExpectedResponse(registry, appId, "TrackedRequest", "TrackedResponse", within);

        var request = (PublishResult.Accepted)await publish.PublishAsync("TrackedRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);
        var requestEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == request.CorrelationId);

        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == request.CorrelationId);
        Assert.AreEqual("trackedrequest", tracker.RequestEventType);
        Assert.AreEqual("trackedresponse", tracker.ExpectedResponseEventType);
        Assert.AreEqual(requestEvent.AppendedAt + within, tracker.DeadlineAt);
        Assert.IsNull(tracker.SatisfiedByEventId);
        Assert.IsNull(tracker.EscalatedAt);
    }

    public static async Task AMatchingResponsePublishedBeforeTheDeadlineSatisfiesTheTrackerAndNoMissingEventIsEverPublished(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-3";
        var within = TimeSpan.FromHours(1); // comfortably in the future -- this scenario must never escalate
        await RegisterRequestTypeWithExpectedResponse(registry, appId, "AskRequest", "AskResponse", within);

        var request = (PublishResult.Accepted)await publish.PublishAsync("AskRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // opens the tracker row

        var response = (PublishResult.Accepted)await publish.PublishAsync(
            "AskResponse", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null, RespondsToEventId: request.CorrelationId), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // satisfies it

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == request.CorrelationId);
        Assert.AreEqual(response.CorrelationId, tracker.SatisfiedByEventId);
        Assert.IsNotNull(tracker.SatisfiedAt);
        Assert.IsNull(tracker.EscalatedAt, "a response that arrived on time must never be escalated");

        var missingCount = await db.Events.CountAsync(e => e.AppId == appId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant());
        Assert.AreEqual(0, missingCount, "no ExpectedResponseMissing may ever be published for a request that was satisfied on time");
    }

    public static async Task NoMatchingResponseAtAllResultsInExactlyOneExpectedResponseMissingCarryingRespondsToEventIdBackAtTheRequest(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-4";
        var within = TimeSpan.FromMilliseconds(1); // already overdue by the time the sweep runs
        await RegisterRequestTypeWithExpectedResponse(registry, appId, "SilentRequest", "SilentResponse", within);

        var request = (PublishResult.Accepted)await publish.PublishAsync("SilentRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // opens the tracker row
        await Task.Delay(TimeSpan.FromMilliseconds(20)); // clears the 1ms Within comfortably

        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // sweeps and escalates
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // a second sweep -- must not double-publish

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == request.CorrelationId);
        Assert.IsNull(tracker.SatisfiedByEventId);
        Assert.IsNotNull(tracker.EscalatedAt);

        var missingEvents = await db.Events
            .Where(e => e.AppId == appId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant())
            .ToListAsync();
        Assert.AreEqual(1, missingEvents.Count, "exactly one ExpectedResponseMissing per tracker row, even across repeated sweeps");
        Assert.AreEqual(request.CorrelationId, missingEvents[0].RespondsToEventId, "ExpectedResponseMissing must set RespondsToEventId back at the original request");
    }

    public static async Task ALateResponseArrivingAfterEscalationIsStillRecordedNeverTreatedAsAnError(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-5";
        var within = TimeSpan.FromMilliseconds(1);
        await RegisterRequestTypeWithExpectedResponse(registry, appId, "LateRequest", "LateResponse", within);

        var request = (PublishResult.Accepted)await publish.PublishAsync("LateRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // escalates -- ExpectedResponseMissing already published

        var lateResponse = (PublishResult.Accepted)await publish.PublishAsync(
            "LateResponse", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null, RespondsToEventId: request.CorrelationId), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == request.CorrelationId);
        Assert.AreEqual(lateResponse.CorrelationId, tracker.SatisfiedByEventId, "a late response is still recorded, never dropped, even after its own tracker was already escalated");
        Assert.IsNotNull(tracker.SatisfiedAt);
        Assert.IsNotNull(tracker.EscalatedAt, "already-fired escalation is never retracted just because a late response showed up afterward");
    }

    public static async Task KillingAndRestartingTheWatcherMidSweepLosesNoTrackerStateAndNeverDoublePublishes(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db)
    {
        const string appId = "expected-response-demo-6";
        var within = TimeSpan.FromMilliseconds(1);
        await RegisterRequestTypeWithExpectedResponse(registry, appId, "RestartRequest", "RestartResponse", within);

        var request = (PublishResult.Accepted)await publish.PublishAsync("RestartRequest", new PublishEventRequest(appId, 1, """{ "Id": "x" }""", null, null), TestClaimsPrincipal.None);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish); // escalates

        // Simulates a fresh process picking the lease back up -- a brand
        // new DbContext-backed call, not the same in-memory instance, the
        // same "restart" proxy RouterWorker's own tests use elsewhere in
        // this suite (there is no separate process to actually kill here).
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);
        await ExpectedResponseWatcher.RunOnceAsync(db, registry, publish);

        var tracker = await db.ExpectedResponseTrackers.AsNoTracking().SingleAsync(t => t.RequestEventId == request.CorrelationId);
        Assert.IsNotNull(tracker.EscalatedAt);
        var missingCount = await db.Events.CountAsync(e => e.AppId == appId && e.EventType == ExpectedResponseMissingEventType.Name.ToLowerInvariant());
        Assert.AreEqual(1, missingCount, "repeated ticks against durable, already-escalated tracker state must never re-publish");
    }
}
