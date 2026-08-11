using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.Webhooks;
using EventStore.Interchange.Abstractions;
using EventStore.LeaderElection;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Webhooks;

// ADR-060/078 -- "WebhookOutboxPump" is the 4th of ADR-078's 4 named
// worker roles, drains the durable WebhookOutbox one subscription at a
// time. Unlike UpcastMaterializer (folded into "Router"'s own lease, since
// it was never independently schedulable to begin with), this genuinely IS
// its own independent process, so it gets its own lease -- exactly as
// "Leader Election via Database-Backed Lease"'s own build-plan section
// already anticipated when it deferred this 4th role to this item.
public class WebhookOutboxPump(
    IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, WebhookRetryTracker retryTracker, ILogger<WebhookOutboxPump> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private const string WorkerRole = "WebhookOutboxPump";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(2.5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isLeader = false;
        var nextRenewalAt = DateTimeOffset.MinValue;
        var httpClient = httpClientFactory.CreateClient("Webhooks");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();

                if (DateTimeOffset.UtcNow >= nextRenewalAt)
                {
                    var leaderElection = scope.ServiceProvider.GetRequiredService<LeaderElectionService>();
                    var acquired = await leaderElection.TryAcquireOrRenewAsync(WorkerRole, LeaseHolderId.Current, LeaseDuration, stoppingToken);
                    if (acquired != isLeader)
                    {
                        isLeader = acquired;
                        logger.LogInformation("Webhook outbox pump {State} the {WorkerRole} lease", isLeader ? "acquired" : "lost", WorkerRole);
                    }
                    nextRenewalAt = isLeader ? DateTimeOffset.UtcNow + RenewInterval : DateTimeOffset.MinValue;
                }

                if (isLeader)
                {
                    var schemaRegistry = scope.ServiceProvider.GetRequiredService<SchemaRegistryService>();
                    var payloadMasker = scope.ServiceProvider.GetRequiredService<IPayloadMasker>();
                    var options = scope.ServiceProvider.GetRequiredService<IOptions<WebhookOptions>>();
                    await RunOnceAsync(db, httpClient, schemaRegistry, payloadMasker, options.Value, retryTracker, scope.ServiceProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook outbox pump tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    // One tick: attempt exactly the OLDEST pending row for every Active
    // subscription. Head-of-line blocking within one subscription is
    // deliberate -- Standard Webhooks/most real providers deliver a given
    // target's events in order; a target that's slow/down delays only ITS
    // OWN subscription, never another's (a separate WebhookDeliveryCursor
    // per subscription already guarantees that).
    public static async Task RunOnceAsync(
        EventStoreContext db, HttpClient httpClient, SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker,
        WebhookOptions options, WebhookRetryTracker retryTracker, IServiceProvider? serviceProvider = null, CancellationToken ct = default)
    {
        var subscriptions = await db.WebhookSubscriptions.Where(s => s.Active).ToListAsync(ct);
        foreach (var subscription in subscriptions)
            await DeliverNextAsync(db, httpClient, schemaRegistry, payloadMasker, options, retryTracker, subscription, serviceProvider, ct);
    }

    private static async Task DeliverNextAsync(
        EventStoreContext db, HttpClient httpClient, SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker,
        WebhookOptions options, WebhookRetryTracker retryTracker, WebhookSubscription subscription, IServiceProvider? serviceProvider, CancellationToken ct)
    {
        var cursor = await db.WebhookDeliveryCursors.SingleOrDefaultAsync(c => c.SubscriptionId == subscription.SubscriptionId, ct);
        var isNewCursor = cursor is null;
        cursor ??= new WebhookDeliveryCursor { SubscriptionId = subscription.SubscriptionId };

        var next = await db.WebhookOutbox
            .Where(o => o.SubscriptionId == subscription.SubscriptionId && o.SequenceNumber > cursor.LastDeliveredSequenceNumber)
            .OrderBy(o => o.SequenceNumber)
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return;

        var now = DateTimeOffset.UtcNow;
        if (retryTracker.ShouldWait(subscription.SubscriptionId, next.SequenceNumber, now))
            return;

        // ADR-057/060 -- re-mask from the ORIGINAL StoredEvent on every
        // attempt (first try or retry alike), never resend the value
        // captured at enqueue time verbatim: IPayloadMasker's own reveal
        // path checks the erasure key's CURRENT state each call, so a
        // crypto-shredding erasure that lands between enqueue and a
        // successful delivery is correctly reflected as {"erased": true}
        // on the very next attempt, not silently missed.
        var sourceEvent = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.SequenceNumber == next.SourceSequenceNumber, ct);
        next.EventPayloadSnapshot = sourceEvent is null
            ? next.EventPayloadSnapshot
            : await RemaskAsync(schemaRegistry, payloadMasker, subscription, sourceEvent, ct);

        // ADR-072 -- an outbound adapter transforms the event into an
        // external wire format as an extra step IMMEDIATELY BEFORE
        // delivery, composing with delivery/signing/retry unchanged: the
        // masked JSON above (EventPayloadSnapshot, the delivery-history
        // record and what re-masking/erasure-retry logic always operates
        // on) is never replaced by this -- only the bytes actually POSTed
        // and signed are. A target expecting XML must see a signature
        // computed over the SAME XML bytes it received, never the JSON.
        // A misconfigured/failing adapter (an unregistered key, or one
        // that throws NotSupportedException for this direction) fails
        // THIS delivery attempt only -- retried with backoff, eventually
        // dead-lettered -- never a silent fallback to untransformed JSON
        // the target isn't expecting, and never an unhandled exception
        // that would abort every OTHER subscription's own tick too.
        cursor.LastAttemptAt = now;
        bool success;
        string? lastError;
        try
        {
            var (wireBody, contentType) = subscription.OutboundAdapterKey is { } adapterKey && serviceProvider is not null && sourceEvent is not null
                ? await ApplyOutboundAdapterAsync(serviceProvider, adapterKey, subscription.AppId, sourceEvent.EventType, next.EventPayloadSnapshot, ct)
                : (next.EventPayloadSnapshot, "application/json");

            (success, lastError) = await AttemptDeliveryAsync(httpClient, subscription, wireBody, contentType, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            success = false;
            lastError = $"outbound adapter '{subscription.OutboundAdapterKey}' failed: {ex.Message}";
        }

        if (success)
        {
            cursor.LastDeliveredSequenceNumber = next.SequenceNumber;
            cursor.LastSuccessAt = now;
            retryTracker.Clear(subscription.SubscriptionId, next.SequenceNumber);
        }
        else
        {
            var attempts = retryTracker.RecordFailure(subscription.SubscriptionId, next.SequenceNumber, options.InitialBackoff, options.MaxBackoff, now);
            if (attempts >= options.MaxAttempts)
            {
                await AppendDeadLetterAsync(db, schemaRegistry, subscription, next, attempts, lastError, ct);
                // Unblocks the subscription -- a permanently-broken target must
                // never head-of-line-block every event behind it forever. The
                // failure is not silently dropped: WebhookDeliveryFailed above
                // is a real, queryable, permanent record of exactly this row.
                cursor.LastDeliveredSequenceNumber = next.SequenceNumber;
                retryTracker.Clear(subscription.SubscriptionId, next.SequenceNumber);
            }
        }

        if (isNewCursor)
            db.WebhookDeliveryCursors.Add(cursor);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<(bool Success, string? LastError)> AttemptDeliveryAsync(
        HttpClient httpClient, WebhookSubscription subscription, string wireBody, string contentType, DateTimeOffset now, CancellationToken ct)
    {
        var (webhookId, timestamp, signature) = WebhookSigner.Sign(wireBody, subscription.SigningSecret, Guid.NewGuid(), now);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl)
            {
                Content = new StringContent(wireBody, Encoding.UTF8, contentType),
            };
            request.Headers.Add("webhook-id", webhookId);
            request.Headers.Add("webhook-timestamp", timestamp);
            request.Headers.Add("webhook-signature", signature);

            using var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? (true, null) : (false, $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }

    // ADR-072 -- resolves the configured IInterchangeFormatAdapter by its
    // registered keyed-DI key and applies its own FormatOutboundAsync
    // transform. A missing/misconfigured key, or an adapter that doesn't
    // actually support outbound (throws NotSupportedException), fails this
    // delivery ATTEMPT the same way an unreachable target would -- retried
    // with backoff, eventually dead-lettered -- never a silent fallback to
    // the untransformed JSON, which the target would not be expecting.
    private static async Task<(string Body, string ContentType)> ApplyOutboundAdapterAsync(
        IServiceProvider serviceProvider, string adapterKey, string appId, string eventType, string maskedPayloadJson, CancellationToken ct)
    {
        var adapter = serviceProvider.GetRequiredKeyedService<IInterchangeFormatAdapter>(adapterKey);
        var payloadNode = JsonNode.Parse(maskedPayloadJson);
        var transformed = await adapter.FormatOutboundAsync(appId, eventType, payloadNode, ct);
        return (transformed, "application/xml");
    }

    private static async Task<string> RemaskAsync(
        SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker, WebhookSubscription subscription, StoredEvent sourceEvent, CancellationToken ct)
    {
        var definition = await schemaRegistry.GetVersionAsync(sourceEvent.AppId, sourceEvent.EventType, sourceEvent.SchemaVersion, ct);
        if (definition is null)
            return "null"; // the declared schema is no longer resolvable -- nothing safe to (re)send

        var schemaNode = JsonNode.Parse(definition.JsonSchema)!;
        var payloadNode = JsonNode.Parse(sourceEvent.Payload);
        var hasClaim = WebhookSubscriptionService.BuildHasClaim(subscription.FixedClaimsSnapshot);
        var masked = await payloadMasker.MaskAsync(schemaNode, payloadNode, sourceEvent.EntityId, hasClaim, ct);
        return masked?.ToJsonString() ?? "null";
    }

    // Appended directly via EventAppender, WITH Status "received" -- Router's
    // own next tick validates/folds it exactly like any ordinary publish,
    // resolving its EntityId per WebhookDeliveryFailedEventType's own
    // EntityIdField. Deliberately NOT PublishService.PublishAsync: that
    // would require EventStore.Webhooks to depend on EventStore.Inbox,
    // which itself depends on EventStore.Router (EntityIdResolver) -- and
    // Router already depends on EventStore.Webhooks for WebhookEnqueueResolver,
    // a genuine circular project reference. EventAppender is the same
    // lower-level primitive UpcastMaterializer/PeerSyncReceiver already
    // bypass PublishService for, for their own, different reasons.
    private static async Task AppendDeadLetterAsync(
        EventStoreContext db, SchemaRegistryService schemaRegistry, WebhookSubscription subscription, WebhookOutbox row, int attempts, string? lastError, CancellationToken ct)
    {
        await WebhookDeliveryFailedEventType.EnsureRegisteredAsync(schemaRegistry, subscription.AppId, ct);

        var eventType = WebhookDeliveryFailedEventType.Name.ToLowerInvariant();
        var failureKey = $"{subscription.SubscriptionId}:{row.SourceSequenceNumber}";
        var payload = JsonSerializer.Serialize(new
        {
            SubscriptionId = subscription.SubscriptionId.ToString(),
            TargetSequenceNumber = row.SourceSequenceNumber,
            Attempts = attempts,
            LastError = lastError ?? "unknown",
            FailureKey = failureKey,
        });

        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            AppId = subscription.AppId,
            EntityId = "", // resolved by Router once it folds this event (ADR-021), the same "starts empty" convention PublishService's own ordinary inserts use
            EventType = eventType,
            SchemaVersion = 1,
            Payload = payload,
            PayloadHash = EventPayloadHash.Compute(eventType, payload, []),
            ChainHash = "", // computed by EventAppender, once SequenceNumber is known
            Status = "received", // Router's own next tick folds/validates this, exactly like any ordinary publish
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = "system:webhook-outbox-pump",
        };

        await EventAppender.AppendAsync(db, storedEvent, [], ct);
    }
}
