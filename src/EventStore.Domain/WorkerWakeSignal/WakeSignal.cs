namespace EventStore.Domain.WorkerWakeSignal;

// ADR-095 -- one durable "there was a signal at time T" marker per worker
// role/topic, deployment-wide (the same LeaderLease shape ADR-078 already
// establishes, not AppId-scoped -- ADR-075's silo model means no per-AppId
// concept here either). Exists specifically for IWorkerWakeSignal's SQLite
// implementation, which has no cross-process notification primitive at all
// (unlike Postgres LISTEN/NOTIFY or SQL Server Service Broker) -- this row
// is what an in-process Channel<T> wake gets checked against on startup, so
// a signal that fired while nothing was listening (a brief restart window)
// is still visible via LastSignaledAt rather than silently lost. Postgres/
// SQL Server implementations don't depend on this table for correctness at
// all (their own native transport already durably queues/notifies); this
// project's own migrations still create it uniformly on every provider,
// the same "avoid a second wave of migrations" precedent StoredEvent/
// EntityStoreRow already established, in case a future provider ever needs
// the identical fallback SQLite does.
public class WakeSignal
{
    public string Topic { get; set; } = default!; // primary key -- e.g. "router"
    public DateTimeOffset LastSignaledAt { get; set; }
}
