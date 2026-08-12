namespace EventStore.WorkerWakeSignal;

// ADR-095 -- a "wake sooner" layer on top of every background worker's own
// existing poll loop, never a replacement for it: the poll loop stays the
// sole correctness guarantee (a missed/lost signal just means a worker
// waits its full, already-safe poll interval, exactly like before this
// existed), the same "notify-to-wake, poll-to-confirm" shape Postgres
// LISTEN/NOTIFY's own real-world usage pattern already establishes. One
// implementation per provider (docs/10-open-questions.md's own resolved
// row): SqliteWorkerWakeSignal (in-process only -- SQLite has no
// cross-process notification primitive at all), PostgresWorkerWakeSignal
// (LISTEN/NOTIFY), SqlServerWorkerWakeSignal (Service Broker).
public interface IWorkerWakeSignal
{
    // Signals that new work exists for `topic` -- called by a publisher
    // (PublishService) right after a durable write actually succeeds, never
    // before. Best-effort: a failure here must never fail the caller's own
    // write, since the poll loop finds the same work regardless.
    Task NotifyAsync(string topic, CancellationToken ct = default);

    // Waits until EITHER a signal for `topic` arrives, or `maxWait`
    // elapses, whichever comes first -- a worker calls this in place of an
    // unconditional Task.Delay(pollInterval) between ticks that found
    // nothing to do.
    Task WaitForWakeAsync(string topic, TimeSpan maxWait, CancellationToken ct = default);
}
