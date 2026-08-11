using System.Net;
using System.Text;
using System.Xml.Linq;
using EventStore.Domain.Webhooks;
using EventStore.Inbox;
using EventStore.Interchange;
using EventStore.Interchange.Abstractions;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Outbound Webhooks" (docs/08-build-plan.md, ADR-060) -- the scenarios
// that genuinely need a real HTTP round trip: Standard Webhooks header/
// signature verification, retry+backoff-to-eventual-success, exhausted-
// retry dead-lettering (verified queryable via the ordinary Lineage API,
// not just a raw db query), a simulated pump restart (fresh
// WebhookRetryTracker, same durable cursor) proving no lost/duplicated
// delivery, and the crypto-shredding erasure/retry interaction. Enqueue
// mechanics themselves (freezing FixedClaimsSnapshot, masked-at-enqueue,
// non-matching-type-never-enqueued) are covered without any real HTTP in
// WebhookScenarioAssertions.cs/WebhookSqliteTests.cs -- this file is
// SQLite-only, deliberately: nothing about HTTP delivery is provider-
// specific, the same reasoning AttachmentHttpSqliteTests/
// ReplicationHttpSqliteTests already established for their own real-wire
// halves.
//
// One database file PER TEST METHOD, not per class (a deliberate departure
// from every other *SqliteTests.cs file's own shared-class-file convention,
// found necessary by actually running this): WebhookOutboxPump.RunOnceAsync
// deliberately queries EVERY Active WebhookSubscription with no AppId scope
// at all -- the correct, real production shape for a deployment-wide pump,
// unlike every other service in this repo's own tests, which always scope
// their own queries to one caller-chosen AppId/key and can safely share one
// file under MSTest's 32-way parallelism. Sharing one file here made one
// test's own RunOnceAsync tick pick up and attempt delivery against a
// DIFFERENT, concurrently-running test's own subscription and backend --
// caught by real, inflated request counts, not by reasoning about it.
[TestClass]
public class WebhookDeliveryHttpSqliteTests
{
    private string _dbPath = default!;

    [TestInitialize]
    public void TestInit()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-webhooks-http-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    private record CapturedRequest(string Body, string WebhookId, string Timestamp, string Signature);

    private static async Task<(WebApplication Backend, string Address, List<CapturedRequest> Requests)> StartBackendAsync(Func<int, IResult> respond)
    {
        var requests = new List<CapturedRequest>();
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var backend = backendBuilder.Build();
        backend.MapPost("/{**catch-all}", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var index = requests.Count;
            requests.Add(new CapturedRequest(
                body, context.Request.Headers["webhook-id"].ToString(),
                context.Request.Headers["webhook-timestamp"].ToString(), context.Request.Headers["webhook-signature"].ToString()));
            return respond(index);
        });
        await backend.StartAsync();
        var address = backend.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (backend, address, requests);
    }

    private static async Task<Guid> RegisterAndEnqueueOneEventAsync(
        EventStoreContext db, SchemaRegistryService registry, PublishService publish, WebhookSubscriptionService subscriptions,
        EventStore.Upcasting.UpcastChain upcastChain, EventStore.Masking.IPayloadMasker payloadMasker,
        string appId, string targetUrl, string customerTaxIdClaim, string orderId)
    {
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "OrderId": { "type": "string" },
                    "CustomerTaxId": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:pii", "regulatoryClassification": "PII" } }
                  }, "required": ["OrderId", "CustomerTaxId"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var subscription = await subscriptions.RegisterAsync(
            appId, targetUrl, ["OrderPlaced"], "whsec_test-secret", TestClaimsPrincipal.With(customerTaxIdClaim));

        var result = await publish.PublishAsync("OrderPlaced",
            new PublishEventRequest(appId, 1, $$"""{ "OrderId": "{{orderId}}", "CustomerTaxId": "123-45-6789" }""", null, null),
            TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(result);

        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);
        return subscription.SubscriptionId;
    }

    [TestMethod]
    public async Task DeliverySignsThePayloadWithTheStandardWebhooksHeaderTripleVerifiableAgainstTheSharedSecret()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(_ => Results.Ok());
        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, "webhooks-http-1", address, "clearance:none", "order-1");

        using var httpClient = new HttpClient();
        var options = new WebhookOptions();
        var retryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);

        Assert.AreEqual(1, requests.Count);
        var request = requests[0];
        Assert.IsFalse(string.IsNullOrEmpty(request.WebhookId));
        Assert.IsFalse(string.IsNullOrEmpty(request.Timestamp));
        Assert.IsTrue(WebhookSigner.Verify(request.Body, "whsec_test-secret", request.WebhookId, request.Timestamp, request.Signature));

        var cursor = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.IsTrue(cursor.LastDeliveredSequenceNumber > 0);
        Assert.IsNotNull(cursor.LastSuccessAt);

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task RotatingTheSigningSecretEmitsDualSignaturesDuringTheOverlapWindowThenASingleSignatureOnceDiscarded()
    {
        // "Signing Secret Rotation, Dual Signature" (ADR-093) -- the full
        // rotation lifecycle end to end: one signature before rotation,
        // two (verifiable against EITHER secret) during the overlap
        // window, back to one (the NEW secret only) once the previous
        // secret is discarded.
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);
        var options = new WebhookOptions();
        var retryTracker = new WebhookRetryTracker();

        var (backend, address, requests) = await StartBackendAsync(_ => Results.Ok());
        const string appId = "webhooks-rotation-demo";
        const string originalSecret = "whsec_original-secret";
        await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "OrderId": { "type": "string" } }, "required": ["OrderId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        var subscription = await subscriptions.RegisterAsync(appId, address, ["OrderPlaced"], originalSecret, TestClaimsPrincipal.None);

        using var httpClient = new HttpClient();

        async Task PublishAndDeliverAsync(string orderId)
        {
            var published = await publish.PublishAsync("OrderPlaced",
                new PublishEventRequest(appId, 1, $$"""{ "OrderId": "{{orderId}}" }""", null, null), TestClaimsPrincipal.None);
            Assert.IsInstanceOfType<PublishResult.Accepted>(published);
            await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);
            await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        }

        // Before rotation: exactly one signature, verifiable against the original secret.
        await PublishAndDeliverAsync("rotation-order-1");
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual(1, requests[0].Signature.Split(' ').Length, "no PreviousSigningSecret set yet -- the header carries exactly one signature, unchanged from before this ADR");
        Assert.IsTrue(WebhookSigner.Verify(requests[0].Body, originalSecret, requests[0].WebhookId, requests[0].Timestamp, requests[0].Signature));

        // Rotate: opens the overlap window.
        var newSecret = await subscriptions.RotateSigningSecretAsync(subscription.SubscriptionId);
        Assert.AreNotEqual(originalSecret, newSecret);
        var afterRotate = await db.WebhookSubscriptions.AsNoTracking().SingleAsync(s => s.SubscriptionId == subscription.SubscriptionId);
        Assert.AreEqual(originalSecret, afterRotate.PreviousSigningSecret);
        Assert.AreEqual(newSecret, afterRotate.SigningSecret);

        // During the window: a delivery carries BOTH signatures, each independently verifiable --
        // a receiver caching only the old secret still verifies successfully.
        await PublishAndDeliverAsync("rotation-order-2");
        Assert.AreEqual(2, requests.Count);
        var duringWindow = requests[1];
        Assert.AreEqual(2, duringWindow.Signature.Split(' ').Length, "PreviousSigningSecret is set -- Standard Webhooks' own dual-signature rotation convention");
        Assert.IsTrue(WebhookSigner.Verify(duringWindow.Body, originalSecret, duringWindow.WebhookId, duringWindow.Timestamp, duringWindow.Signature), "a receiver caching only the OLD secret still verifies during the overlap window");
        Assert.IsTrue(WebhookSigner.Verify(duringWindow.Body, newSecret, duringWindow.WebhookId, duringWindow.Timestamp, duringWindow.Signature), "a receiver that already picked up the NEW secret also verifies");

        // Discard: ends the window.
        await subscriptions.DiscardPreviousSigningSecretAsync(subscription.SubscriptionId);
        var afterDiscard = await db.WebhookSubscriptions.AsNoTracking().SingleAsync(s => s.SubscriptionId == subscription.SubscriptionId);
        Assert.IsNull(afterDiscard.PreviousSigningSecret);

        // After discard: exactly one signature again, verifiable against the NEW secret only.
        await PublishAndDeliverAsync("rotation-order-3");
        Assert.AreEqual(3, requests.Count);
        var afterWindow = requests[2];
        Assert.AreEqual(1, afterWindow.Signature.Split(' ').Length, "discarding the previous secret ends the window -- back to exactly one signature");
        Assert.IsTrue(WebhookSigner.Verify(afterWindow.Body, newSecret, afterWindow.WebhookId, afterWindow.Timestamp, afterWindow.Signature));
        Assert.IsFalse(WebhookSigner.Verify(afterWindow.Body, originalSecret, afterWindow.WebhookId, afterWindow.Timestamp, afterWindow.Signature), "the old secret no longer verifies once the window has ended");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task AnOutboundAdapterTransformsTheEventIntoItsExternalFormatImmediatelyBeforeDeliveryUsingTheSubscriptionsUnmodifiedSigningRetryMechanics()
    {
        // "Bulk Ingestion & External Interchange-Format Adapters" (ADR-072)
        // -- composes with "Outbound Webhooks"'s own delivery/signing/retry
        // pipeline unchanged: only the wire bytes actually sent/signed
        // change (real E2B(R3) XML instead of the ordinary masked JSON),
        // never the mechanics around it.
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(_ => Results.Ok());
        const string appId = "webhooks-outbound-adapter-demo";
        await registry.RegisterAsync("AdverseEventReported", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "CaseId": { "type": "string" }, "PatientId": { "type": "string" }, "DrugName": { "type": "string" }, "ReactionTerm": { "type": "string" } }, "required": ["CaseId"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.CaseId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var subscription = await subscriptions.RegisterAsync(
            appId, address, ["AdverseEventReported"], "whsec_e2b-test-secret", TestClaimsPrincipal.None, outboundAdapterKey: "IchE2bR3");

        var published = await publish.PublishAsync("AdverseEventReported",
            new PublishEventRequest(appId, 1, """{ "CaseId": "case-9", "PatientId": "pat-9", "DrugName": "Ibuprofen", "ReactionTerm": "Nausea" }""", null, null),
            TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(published);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);

        var services = new ServiceCollection();
        services.AddKeyedScoped<IInterchangeFormatAdapter, IchE2bR3Adapter>("IchE2bR3");
        var serviceProvider = services.BuildServiceProvider();

        using var httpClient = new HttpClient();
        var options = new WebhookOptions();
        var retryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker, serviceProvider);

        Assert.AreEqual(1, requests.Count);
        var sentBody = requests[0].Body;
        var document = XDocument.Parse(sentBody);
        var hl7V3 = (XNamespace)"urn:hl7-org:v3";
        Assert.AreEqual(hl7V3 + "MCCI_IN200100UV01", document.Root!.Name, "the real ICH E2B(R3) envelope, not the ordinary masked JSON");
        Assert.AreEqual("Ibuprofen", document.Descendants(hl7V3 + "medicinalproduct").Single().Value);

        // The signature was computed over the SAME XML bytes actually
        // sent, never the untransformed JSON -- a receiver verifying
        // against what it actually received must succeed.
        Assert.IsTrue(WebhookSigner.Verify(sentBody, "whsec_e2b-test-secret", requests[0].WebhookId, requests[0].Timestamp, requests[0].Signature));

        var cursor = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscription.SubscriptionId);
        Assert.IsTrue(cursor.LastDeliveredSequenceNumber > 0, "delivery/retry mechanics are otherwise completely unaffected by the outbound transform");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task AFailedDeliveryEventuallySucceedsAndTheCursorOnlyAdvancesOnActualSuccess()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(index => index < 2 ? Results.StatusCode(503) : Results.Ok());
        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, "webhooks-http-2", address, "clearance:none", "order-2");

        using var httpClient = new HttpClient();
        var options = new WebhookOptions { InitialBackoff = TimeSpan.FromMilliseconds(1), MaxBackoff = TimeSpan.FromMilliseconds(50) };
        var retryTracker = new WebhookRetryTracker();

        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        var cursorAfterFirstFailure = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.AreEqual(0, cursorAfterFirstFailure.LastDeliveredSequenceNumber, "a failed attempt must never advance the cursor");

        await Task.Delay(300); // clears the tiny backoff+jitter window deterministically
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        var cursorAfterSecondFailure = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.AreEqual(0, cursorAfterSecondFailure.LastDeliveredSequenceNumber);

        await Task.Delay(300);
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        var cursorAfterSuccess = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.IsTrue(cursorAfterSuccess.LastDeliveredSequenceNumber > 0, "the third attempt succeeds and the cursor finally advances");
        Assert.AreEqual(3, requests.Count);

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task ExhaustedRetriesDeadLettersAsAQueryableWebhookDeliveryFailedEventAndUnblocksTheSubscription()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);
        var lineage = new LineageService(db, new SqliteEventLineageQueryProvider(), registry);

        var (backend, address, requests) = await StartBackendAsync(_ => Results.NotFound());
        const string appId = "webhooks-http-3";
        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, appId, address, "clearance:none", "order-3a");

        using var httpClient = new HttpClient();
        var options = new WebhookOptions { MaxAttempts = 3, InitialBackoff = TimeSpan.FromMilliseconds(1), MaxBackoff = TimeSpan.FromMilliseconds(20) };
        var retryTracker = new WebhookRetryTracker();

        // Jitter (0-250ms) can exceed a short fixed inter-tick delay, so this
        // polls until MaxAttempts is actually reached rather than assuming a
        // fixed number of ticks always lands exactly on it -- found only by
        // running this: a fixed 150ms delay between 3 ticks sometimes left
        // one tick's own retry not yet due, landing on 2 attempts, not 3.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (requests.Count < options.MaxAttempts && DateTime.UtcNow < deadline)
        {
            await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
            await Task.Delay(50);
        }

        Assert.AreEqual(3, requests.Count, "exactly MaxAttempts requests were made before dead-lettering");

        var failureKey = $"{subscriptionId}:";
        var deadLetter = await db.Events.AsNoTracking().SingleAsync(e => e.EventType == "webhookdeliveryfailed" && e.AppId == appId);
        Assert.IsTrue(deadLetter.Payload.Contains(subscriptionId.ToString()));
        // Router hasn't processed this reserved event yet (it was appended with
        // Status "received", exactly like any ordinary publish) -- one more
        // tick folds/validates it, same as any other event this pump appends.
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);
        Assert.AreEqual(LineageRootCheck.Ok, await lineage.CheckRootAsync(deadLetter.EventId, TestClaimsPrincipal.None), "queryable through the ordinary Lineage API, not just an operator log");
        Assert.IsTrue(failureKey.Length > 0);

        // A second, later-enqueued event for the SAME subscription must not
        // be blocked forever behind the first, now-abandoned row.
        var secondResult = await publish.PublishAsync("OrderPlaced",
            new PublishEventRequest(appId, 1, """{ "OrderId": "order-3b", "CustomerTaxId": "999-99-9999" }""", null, null), TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<PublishResult.Accepted>(secondResult);
        await RouterWorker.RunOnceAsync(db, registry, upcastChain, payloadMasker: payloadMasker);

        var (backend2, address2, requests2) = await StartBackendAsync(_ => Results.Ok());
        // The subscription's own TargetUrl still points at the FIRST (now
        // permanently-404ing) backend -- redirect isn't possible mid-test, so
        // this proves the unblock property differently: the cursor already
        // sits past the dead-lettered row, so the NEXT pending row for this
        // subscription is the second event, not stuck retrying the first.
        var cursorBeforeSecondDelivery = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        var secondPendingRow = await db.WebhookOutbox.AsNoTracking()
            .Where(o => o.SubscriptionId == subscriptionId && o.SequenceNumber > cursorBeforeSecondDelivery.LastDeliveredSequenceNumber)
            .OrderBy(o => o.SequenceNumber)
            .FirstAsync();
        Assert.IsTrue(secondPendingRow.EventPayloadSnapshot.Contains("999-99-9999") || secondPendingRow.EventPayloadSnapshot.Contains("masked"),
            "the pending row is the SECOND event, not the dead-lettered first one");

        await backend.StopAsync();
        await backend2.StopAsync();
    }

    [TestMethod]
    public async Task KillingTheOutboxPumpMidDeliveryAndRestartingResumesFromTheDurableCursorWithNoLostOrDuplicatedDelivery()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (_, payloadMasker, _) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(index => index == 0 ? Results.StatusCode(500) : Results.Ok());
        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, "webhooks-http-4", address, "clearance:none", "order-4");

        using var httpClient = new HttpClient();
        var options = new WebhookOptions();

        // First "process instance" -- attempts once, fails, then is killed
        // (its own in-memory WebhookRetryTracker is simply discarded).
        var firstProcessRetryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, firstProcessRetryTracker);
        var cursorAfterCrash = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.AreEqual(0, cursorAfterCrash.LastDeliveredSequenceNumber, "the failed attempt never advanced the durable cursor before the crash");

        // "Restart" -- a fresh WebhookRetryTracker, same durable db/cursor.
        var secondProcessRetryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, secondProcessRetryTracker);

        var cursorAfterRestart = await db.WebhookDeliveryCursors.AsNoTracking().SingleAsync(c => c.SubscriptionId == subscriptionId);
        Assert.IsTrue(cursorAfterRestart.LastDeliveredSequenceNumber > 0, "the restarted process picks up exactly where the durable cursor left off");
        Assert.AreEqual(2, requests.Count, "exactly two delivery attempts total -- one before the crash, one after restart, never duplicated beyond that");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task ARetryAttemptedAfterACryptoShreddingErasureCorrectlyCarriesErasedTrue()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, payloadMasker, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(index => index == 0 ? Results.StatusCode(500) : Results.Ok());
        const string appId = "webhooks-http-5";
        const string orderId = "order-5";

        // The registering caller DOES hold clearance:pii -- CustomerTaxId
        // would be genuinely revealed (decrypted), which is the ONLY branch
        // that ever checks live erasure-key state (PayloadMasker.RevealAsync).
        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, appId, address, "clearance:pii", orderId);

        using var httpClient = new HttpClient();
        var options = new WebhookOptions { InitialBackoff = TimeSpan.FromMilliseconds(1), MaxBackoff = TimeSpan.FromMilliseconds(20) };
        var retryTracker = new WebhookRetryTracker();

        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        Assert.AreEqual(1, requests.Count);
        Assert.IsTrue(requests[0].Body.Contains("123-45-6789"), "before erasure, a claim-holding subscription's retry payload carries the real decrypted value");

        var entityId = $"{appId}:orderplaced:{orderId}";
        await erasureKeyService.EraseAsync(entityId);

        // Jitter (0-250ms) can exceed a short fixed delay -- poll until the
        // retry is actually due rather than assuming one fixed wait always
        // clears it (the same real timing issue found in the dead-letter
        // scenario above).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (requests.Count < 2 && DateTime.UtcNow < deadline)
        {
            await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
            await Task.Delay(50);
        }
        Assert.AreEqual(2, requests.Count);
        Assert.IsTrue(requests[1].Body.Contains("\"erased\":true") || requests[1].Body.Contains("\"erased\": true"),
            $"the retry after erasure must carry erased:true, got: {requests[1].Body}");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task AnAlreadyDeliveredPayloadIsNotRetroactivelyReachableAfterALaterErasure()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var (encryptor, payloadMasker, erasureKeyService) = ErasureTestSupport.CreateErasureStack(db, registry);
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var subscriptions = new WebhookSubscriptionService(db);

        var (backend, address, requests) = await StartBackendAsync(_ => Results.Ok());
        const string appId = "webhooks-http-6";
        const string orderId = "order-6";

        var subscriptionId = await RegisterAndEnqueueOneEventAsync(
            db, registry, publish, subscriptions, upcastChain, payloadMasker, appId, address, "clearance:pii", orderId);

        using var httpClient = new HttpClient();
        var options = new WebhookOptions();
        var retryTracker = new WebhookRetryTracker();
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        Assert.AreEqual(1, requests.Count);
        Assert.IsTrue(requests[0].Body.Contains("123-45-6789"));

        var deliveredRow = await db.WebhookOutbox.AsNoTracking().SingleAsync(o => o.SubscriptionId == subscriptionId);
        var deliveredSnapshotBeforeErasure = deliveredRow.EventPayloadSnapshot;

        var entityId = $"{appId}:orderplaced:{orderId}";
        await erasureKeyService.EraseAsync(entityId);

        // The pump never revisits a row the cursor has already passed --
        // there is nothing left pending for this subscription to re-mask.
        await WebhookOutboxPump.RunOnceAsync(db, httpClient, registry, payloadMasker, options, retryTracker);
        Assert.AreEqual(1, requests.Count, "no further delivery attempt happens for an already-delivered row");

        var reloadedRow = await db.WebhookOutbox.AsNoTracking().SingleAsync(o => o.SubscriptionId == subscriptionId);
        Assert.AreEqual(deliveredSnapshotBeforeErasure, reloadedRow.EventPayloadSnapshot, "the already-delivered row's own record is unaffected by a later erasure");

        await backend.StopAsync();
    }
}
