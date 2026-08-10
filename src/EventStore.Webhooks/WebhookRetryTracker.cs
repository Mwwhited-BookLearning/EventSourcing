using System.Collections.Concurrent;

namespace EventStore.Webhooks;

// In-memory, one instance per process (registered as a singleton) --
// deliberately NOT persisted. A restart mid-backoff simply restarts that
// row's own attempt count at 1 on the next tick; WebhookDeliveryCursor
// (the durable state) only ever advances on an actual success or a
// genuinely exhausted dead-letter, so a restart can never lose or
// duplicate a delivery, only reset how many attempts it took to notice
// the target is unreachable -- exactly this item's own "no lost or
// duplicated delivery" exit criterion, which says nothing about attempt
// counts surviving a restart.
public class WebhookRetryTracker
{
    private readonly ConcurrentDictionary<(Guid SubscriptionId, long SequenceNumber), (int Attempts, DateTimeOffset NextAttemptAt)> _state = new();

    public bool ShouldWait(Guid subscriptionId, long sequenceNumber, DateTimeOffset now) =>
        _state.TryGetValue((subscriptionId, sequenceNumber), out var entry) && now < entry.NextAttemptAt;

    // Exponential backoff + jitter, per Standard Webhooks' own recommendation
    // (ADR-060) -- each successive failure waits strictly longer than the
    // last, capped at maxBackoff so a long-broken target doesn't push the
    // next attempt out indefinitely.
    public int RecordFailure(Guid subscriptionId, long sequenceNumber, TimeSpan initialBackoff, TimeSpan maxBackoff, DateTimeOffset now)
    {
        var key = (subscriptionId, sequenceNumber);
        var attempts = (_state.TryGetValue(key, out var existing) ? existing.Attempts : 0) + 1;
        var exponent = Math.Min(attempts - 1, 20); // guards against Ticks overflow on a pathologically high attempt count
        var backoffTicks = Math.Min(initialBackoff.Ticks * (1L << exponent), maxBackoff.Ticks);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        _state[key] = (attempts, now + TimeSpan.FromTicks(backoffTicks) + jitter);
        return attempts;
    }

    public void Clear(Guid subscriptionId, long sequenceNumber) => _state.TryRemove((subscriptionId, sequenceNumber), out _);
}
