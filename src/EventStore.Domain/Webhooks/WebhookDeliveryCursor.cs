namespace EventStore.Domain.Webhooks;

// ADR-060 -- structurally identical to Replication/PeerSyncCursor,
// confirming this really does inherit the durable outbox/inbox primitive
// rather than merely resembling it.
public class WebhookDeliveryCursor
{
    public Guid SubscriptionId { get; set; }
    public long LastDeliveredSequenceNumber { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
}
