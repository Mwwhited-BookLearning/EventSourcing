using System.Collections.Concurrent;
using EventStore.Persistence;
using EventStore.WorkerWakeSignal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventStore.Persistence.Migrations.Postgres;

// ADR-095 -- Postgres's mechanism, revised to match SQL Server Service
// Broker's queue semantics: a signal is a durable row in "WakeSignalQueue"
// (so one sent while no reader is connected is still there on reconnect),
// and NOTIFY is only the doorbell that releases a blocked reader. The
// reader PULLS: it drains the queue first (the RECEIVE equivalent --
// DELETE ... FOR UPDATE SKIP LOCKED), and only when empty blocks on its
// LISTEN connection until notified or maxWait elapses (the WAITFOR ...
// TIMEOUT equivalent). Postgres has no server-side blocking receive nor
// internal activation, so the block lives in Npgsql's WaitAsync.
// RouterWorker's poll loop still backstops everything via maxWait, as with
// SSB. A wake is idempotent, so a notify coalesces into at most one
// pending row per topic and a drain consumes every pending row at once.
// One dedicated LISTEN connection per topic, held open for this process's
// lifetime -- LISTEN is session-scoped, so a fresh connection per call
// would re-subscribe from scratch and could miss a NOTIFY in the gap.
public class PostgresWorkerWakeSignal(EventStoreContext db) : IWorkerWakeSignal
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<NpgsqlConnection>>> ListenConnections = new();

    // Enqueue (unless a signal for this topic is already pending) and ring
    // the doorbell in one command. pg_notify(channel, payload) takes the
    // channel as a real parameter, never string-interpolated SQL. Postgres
    // defers NOTIFY delivery until commit, so a signal never precedes the
    // durable write it announces.
    public async Task NotifyAsync(string topic, CancellationToken ct = default)
    {
        await SweepConsumedAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WakeSignalQueue" ("Topic")
            SELECT {topic} WHERE NOT EXISTS (SELECT 1 FROM "WakeSignalQueue" WHERE "Topic" = {topic} AND "ConsumedAt" IS NULL);
            SELECT pg_notify({topic}, '')
            """, ct);
    }

    public async Task WaitForWakeAsync(string topic, TimeSpan maxWait, CancellationToken ct = default)
    {
        // LISTEN first, so a NOTIFY landing between the drain below and
        // the block is buffered rather than missed.
        var connection = await GetListenConnectionAsync(topic, ct);
        var deadline = DateTimeOffset.UtcNow + maxWait;
        while (true)
        {
            if (await DrainAsync(topic, ct))
                return;

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(remaining);
            try
            {
                await connection.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return; // maxWait elapsed -- the poll loop's own backstop
            }
            catch (Exception ex) when (ex is NpgsqlException or System.IO.IOException)
            {
                // Listen connection died; evict so the next call reopens it.
                // The caller's poll loop covers this tick.
                ListenConnections.TryRemove(topic, out _);
                return;
            }
            // Woken (or a stale notification for an already-drained row):
            // loop -- the drain decides whether it was a real signal.
        }
    }

    // Consumed rows are kept briefly as an audit trail, then deleted. Throttled to once per
    // process per SweepInterval so the hot publish path doesn't pay for it on every notify.
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private static long _lastSweepTicks;

    private async Task SweepConsumedAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastSweepTicks);
        if (now - last < SweepInterval.Ticks || Interlocked.CompareExchange(ref _lastSweepTicks, now, last) != last)
            return;
        var cutoff = DateTimeOffset.UtcNow - Retention;
        await db.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "WakeSignalQueue" WHERE "ConsumedAt" IS NOT NULL AND "ConsumedAt" < {cutoff}""", ct);
    }

    // RECEIVE equivalent: atomically mark consumed (not delete -- swept later) every pending signal for this
    // topic; SKIP LOCKED so concurrent readers never block on each other.
    private async Task<bool> DrainAsync(string topic, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH pending AS (
                SELECT "Id" FROM "WakeSignalQueue" WHERE "Topic" = {topic} AND "ConsumedAt" IS NULL FOR UPDATE SKIP LOCKED)
            UPDATE "WakeSignalQueue" q SET "ConsumedAt" = now() FROM pending WHERE q."Id" = pending."Id"
            """, ct) > 0;

    private Task<NpgsqlConnection> GetListenConnectionAsync(string topic, CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("EventStoreContext has no connection string to open a dedicated LISTEN connection against.");
        var lazy = ListenConnections.GetOrAdd(topic, t => new Lazy<Task<NpgsqlConnection>>(() => CreateListenConnectionAsync(t, connectionString, ct)));
        return lazy.Value;
    }

    private static async Task<NpgsqlConnection> CreateListenConnectionAsync(string topic, string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        // Channel identifiers can't be parameterized in LISTEN itself
        // (unlike NotifyAsync's own pg_notify call) -- safe here because
        // `topic` only ever originates from this codebase's own hardcoded
        // constants, never external input.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"LISTEN \"{topic}\"";
            await command.ExecuteNonQueryAsync(ct);
        }
        return connection;
    }
}
