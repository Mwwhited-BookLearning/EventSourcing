using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Domain.Webhooks;
using EventStore.Masking;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Webhooks;

// ADR-060 -- the "special-purpose reactor" side effect RouterWorker performs
// against every event it processes, the same shape AuthorityDecisionResolver/
// EntityErasureResolver already established: after the event's own ordinary
// fold, check whether it matches any Active WebhookSubscription for this
// AppId and, for each match, mask (IPayloadMasker, unchanged) against that
// subscription's OWN frozen FixedClaimsSnapshot and enqueue a durable
// WebhookOutbox row -- never an in-memory queue (ADR-033's own fault/abend/
// restart-tolerance bar, reused here by inheritance, not resemblance).
//
// Deliberately requires a resolved schema (declaredSchemaNode) to mask
// against -- an event whose own declared version isn't registered at all
// (SchemaStatus "unknown") is never enqueued for delivery, since there is
// no schema to safely mask an unknown shape's sensitive fields against.
// This is a narrower, honestly-stated choice, not implied by ADR-060's own
// text, which never actually considers the unknown-schema case.
public static class WebhookEnqueueResolver
{
    public static async Task ProcessAsync(
        EventStoreContext db, IPayloadMasker payloadMasker, StoredEvent storedEvent, JsonNode? declaredSchemaNode, CancellationToken ct)
    {
        if (declaredSchemaNode is null)
            return;

        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.AppId == storedEvent.AppId && s.Active)
            .ToListAsync(ct);
        if (subscriptions.Count == 0)
            return;

        var payloadNode = JsonNode.Parse(storedEvent.Payload);
        foreach (var subscription in subscriptions)
        {
            if (!subscription.EventTypes.Any(t => string.Equals(t, storedEvent.EventType, StringComparison.OrdinalIgnoreCase)))
                continue;

            var hasClaim = WebhookSubscriptionService.BuildHasClaim(subscription.FixedClaimsSnapshot);
            var masked = await payloadMasker.MaskAsync(declaredSchemaNode, payloadNode, storedEvent.EntityId, hasClaim, ct);

            db.WebhookOutbox.Add(new WebhookOutbox
            {
                SubscriptionId = subscription.SubscriptionId,
                EventPayloadSnapshot = masked?.ToJsonString() ?? "null",
                SourceSequenceNumber = storedEvent.SequenceNumber,
                EnqueuedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}
