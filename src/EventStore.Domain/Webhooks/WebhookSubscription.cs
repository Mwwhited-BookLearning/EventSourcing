namespace EventStore.Domain.Webhooks;

// ADR-060/093 -- shape authority is docs/data/schema-registry.md's
// "Webhook subscriptions" section; keep both in sync in the same pass.
public class WebhookSubscription
{
    public Guid SubscriptionId { get; set; }
    public string AppId { get; set; } = default!;
    public string TargetUrl { get; set; } = default!;
    public string SigningSecret { get; set; } = default!;
    public string? PreviousSigningSecret { get; set; } // set only during an ADR-093 rotation overlap window -- not populated by this item
    public List<string> EventTypes { get; set; } = new();
    public string FixedClaimsSnapshot { get; set; } = default!; // JSON array of "type:value" strings, computed once at registration (ADR-060)
    public bool Active { get; set; } = true;
    public DateTimeOffset RegisteredAt { get; set; }
}
