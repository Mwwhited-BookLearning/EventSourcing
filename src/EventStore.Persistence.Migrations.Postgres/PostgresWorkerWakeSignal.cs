using System.Collections.Concurrent;
using EventStore.Persistence;
using EventStore.WorkerWakeSignal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventStore.Persistence.Migrations.Postgres;

// ADR-095 -- Postgres's own real, native mechanism: NOTIFY (fire-and-forget,
// no durable queue of its own) as the "wake sooner" layer, RouterWorker's
// existing poll loop staying the actual correctness guarantee regardless
// (the "notify-to-wake, poll-to-confirm" pattern docs/10-open-questions.md's
// own resolved row already names). One dedicated LISTEN connection per
// topic, held open for this process's whole lifetime -- LISTEN is
// session-scoped in Postgres, so a fresh connection per call (this class is
// otherwise resolved from a fresh DI scope every tick, the same as every
// other RouterWorker dependency) would re-subscribe from scratch every
// time and could miss a NOTIFY fired in the gap between connections.
public class PostgresWorkerWakeSignal(EventStoreContext db) : IWorkerWakeSignal
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<NpgsqlConnection>>> ListenConnections = new();

    // pg_notify(channel, payload), not a literal `NOTIFY channel` string --
    // the function form accepts the channel name as a real, parameterized
    // argument (ExecuteSqlInterpolatedAsync parameterizes it), never string-
    // interpolated SQL, even though `topic` only ever comes from this
    // codebase's own hardcoded constants (RouterWorker.Topic) today.
    // Issued on EventStoreContext's own connection/ambient transaction --
    // if PublishService's own EventAppender.AppendAsync call already
    // committed by this point (it does -- ADR-011's own Serializable
    // transaction), this fires immediately; if it hadn't, Postgres defers
    // visibility to listeners until commit either way, which is exactly
    // the "never signal before the write is truly durable" contract this
    // interface's own comment requires.
    public async Task NotifyAsync(string topic, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify({topic}, '')", ct);

    public async Task WaitForWakeAsync(string topic, TimeSpan maxWait, CancellationToken ct = default)
    {
        var connection = await GetListenConnectionAsync(topic, ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(maxWait);
        try
        {
            // Blocks until either a NOTIFY on this connection's own LISTEN
            // channel arrives (firing the Notification event synchronously
            // during this call, per Npgsql's own documented behavior) or
            // the linked token cancels.
            await connection.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // maxWait elapsed with no NOTIFY -- the ordinary, expected case
            // every tick this worker's own poll interval already tolerates.
        }
    }

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
