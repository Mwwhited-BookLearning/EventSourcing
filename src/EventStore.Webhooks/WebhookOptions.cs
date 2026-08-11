namespace EventStore.Webhooks;

public class WebhookOptions
{
    // Standard Webhooks' own recommendation: exponential backoff + jitter,
    // not a fixed interval -- ADR-060's own text.
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
}
