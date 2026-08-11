namespace EventStore.Domain.Webhooks;

// ADR-060 -- a durable table, never an in-memory queue; one row per
// (subscription, matching event). Shape authority: docs/data/schema-
// registry.md's "Webhook outbox and delivery cursor" section.
public class WebhookOutbox
{
    public long SequenceNumber { get; set; }
    public Guid SubscriptionId { get; set; }
    public string EventPayloadSnapshot { get; set; } = default!; // masked against FixedClaimsSnapshot -- refreshed on every delivery attempt, not just once at enqueue (see WebhookOutboxPump)
    public long SourceSequenceNumber { get; set; } // FK -> StoredEvent.SequenceNumber -- lets a retry re-mask from the ORIGINAL payload/EntityId, so a crypto-shredding erasure that happens after enqueue but before a successful delivery is correctly reflected
    public DateTimeOffset EnqueuedAt { get; set; }
}
