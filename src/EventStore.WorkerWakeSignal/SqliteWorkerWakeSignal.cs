using System.Collections.Concurrent;
using System.Threading.Channels;
using EventStore.Domain.WorkerWakeSignal;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.WorkerWakeSignal;

// ADR-095 -- SQLite has no cross-process notification primitive at all: no
// separate server process for a LISTEN/NOTIFY or Service Broker-style
// mechanism to attach to, and its own sqlite3_update_hook is process-local
// only (verified against SQLite's own documentation before designing this,
// not assumed). Every Host.Sqlite deployment in this project already runs
// every background worker in the SAME process as the Inbox that publishes,
// so an in-process signal is the entire real mechanism the common case
// needs -- the shared state below connects every scoped PublishService's
// own NotifyAsync call to RouterWorker's one long-lived WaitForWakeAsync
// loop within that ONE Host process.
//
// The durable WakeSignal row exists for a narrower, real edge case: a
// worker that hasn't yet observed ANY wake since ITS OWN process started
// (freshly booted, its in-memory "last observed" state is empty) can still
// notice a signal that already happened -- e.g. a publish landing in the
// brief window between migration/startup and this worker's first
// WaitForWakeAsync call -- without waiting out a full poll interval on its
// very first tick. Once observed once, the in-memory Channel alone carries
// every SUBSEQUENT signal for as long as this process lives.
public class SqliteWorkerWakeSignal(EventStoreContext db) : IWorkerWakeSignal
{
    // Keyed by (connection string, topic), NEVER topic alone -- found live,
    // not assumed: this repo's own integration test suite runs MANY
    // independent WebApplicationFactory-hosted Hosts, each its own
    // completely separate SQLite database file, concurrently in the SAME
    // test process (MSTest's 32-way parallelism). A topic-only key let
    // totally unrelated Hosts' publishers and workers cross-talk through
    // the SAME static Channel, occasionally "stealing" a wake meant for a
    // different Host's RouterWorker and leaving the real target to fall
    // back to its full poll interval -- a real, load-dependent flake this
    // fix closes. A single real Host.Sqlite deployment only ever has one
    // connection string anyway, so this changes nothing about the single-
    // process production shape this class exists for.
    private static readonly ConcurrentDictionary<(string ConnectionString, string Topic), Channel<byte>> Channels = new();
    private static readonly ConcurrentDictionary<(string ConnectionString, string Topic), DateTimeOffset> LastObservedByKey = new();

    private (string ConnectionString, string Topic) KeyFor(string topic) =>
        (db.Database.GetConnectionString() ?? string.Empty, topic);

    private static Channel<byte> ChannelFor((string ConnectionString, string Topic) key) =>
        Channels.GetOrAdd(key, _ => Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }));

    public async Task NotifyAsync(string topic, CancellationToken ct = default)
    {
        // Durable marker first -- a freshly-started worker's own startup
        // check (below) reads this table, so it must be written before the
        // in-process wake, not after, or a narrow race could have a worker
        // finish its startup check just before this row updates.
        var now = DateTimeOffset.UtcNow;
        var updated = await db.WakeSignals.Where(w => w.Topic == topic).ExecuteUpdateAsync(s => s.SetProperty(w => w.LastSignaledAt, now), ct);
        if (updated == 0)
        {
            try
            {
                db.WakeSignals.Add(new Domain.WorkerWakeSignal.WakeSignal { Topic = topic, LastSignaledAt = now });
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another concurrent publisher's own first-ever insert for
                // this topic won the race -- an ordinary "I didn't win this
                // time" outcome, the same shape LeaderElectionService's own
                // first-acquire path already uses.
            }
        }

        ChannelFor(KeyFor(topic)).Writer.TryWrite(0); // best-effort -- a full (already-pending) channel just means a wake is already queued
    }

    public async Task WaitForWakeAsync(string topic, TimeSpan maxWait, CancellationToken ct = default)
    {
        var key = KeyFor(topic);
        if (!LastObservedByKey.ContainsKey(key))
        {
            var row = await db.WakeSignals.AsNoTracking().SingleOrDefaultAsync(w => w.Topic == topic, ct);
            LastObservedByKey[key] = row?.LastSignaledAt ?? DateTimeOffset.MinValue;
            if (row is not null)
                return; // a signal already happened before this worker ever started waiting -- wake immediately, no delay
        }

        var channel = ChannelFor(key);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(maxWait);
        try
        {
            await channel.Reader.ReadAsync(timeoutCts.Token);
            LastObservedByKey[key] = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // maxWait elapsed with no signal -- the ordinary, expected case
            // every tick this worker's own poll interval already tolerates.
        }
    }
}
