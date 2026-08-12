using EventStore.Persistence;
using EventStore.WorkerWakeSignal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence.Migrations.SqlServer;

// ADR-095 -- SQL Server's own real, native mechanism: Service Broker, a
// genuine durable, transactional queue -- RouterWorker's existing poll
// loop still stays the actual correctness guarantee regardless (same
// "notify-to-wake, poll-to-confirm" posture as the Postgres implementation).
//
// Scoped to ONE topic ("router") for this pass, per direct decision to
// prove the mechanism on RouterWorker before wiring the other 5 background
// workers -- a real, named narrowing, not an oversight: one shared queue,
// no per-topic message routing/filtering. Extending to more workers means
// either per-topic queues or a `message_body` topic check on RECEIVE,
// neither built here since there's only one topic to receive today.
//
// Deliberately WITHOUT internal/external activation (`CREATE QUEUE ...
// WITH ACTIVATION (...)`) even though Service Broker supports both real
// mechanisms -- direct decision, this session: RouterWorker's own C#
// WAITFOR/RECEIVE loop IS the active listener the entire time this worker
// runs, so internal activation (auto-invoking a T-SQL stored procedure)
// would just race the C# consumer for the same messages, a real
// correctness hazard, not a missing feature. External activation (waking a
// genuinely separate, possibly non-.NET process that ISN'T continuously
// polling) is the real, different use case Service Broker's own design
// targets that for -- named honestly as not-yet-needed here, since every
// consumer in this build is a live, already-running .NET worker.
public class SqlServerWorkerWakeSignal(EventStoreContext db) : IWorkerWakeSignal
{
    private const string QueueName = "WakeSignalQueue";
    private const string ServiceName = "//EventStore/WakeSignalService";
    private const string MessageTypeName = "//EventStore/WakeSignal";
    private const string ContractName = "//EventStore/WakeSignalContract";

    // A fresh BEGIN DIALOG/SEND/END CONVERSATION per call -- real overhead
    // for a high-frequency publish path, a deliberate, named narrowing for
    // this pass rather than managing a long-lived conversation handle's
    // own lifecycle (which can end unexpectedly from the receiving side,
    // real complexity this pass didn't need to take on to prove the
    // mechanism). `topic` becomes the message body, ignored on receipt
    // since only one topic exists today (see this class's own header
    // comment).
    //
    // Plain string interpolation for the query TEXT (every {..} here is a
    // hardcoded class constant, a real SQL identifier, never a value) --
    // ExecuteSqlInterpolatedAsync's OWN FormattableString parameterization
    // was tried first and is the wrong tool here: it parameterizes EVERY
    // interpolation hole uniformly, including the ones naming real object
    // identifiers inside `[...]` brackets, producing literally invalid SQL
    // like `[@p0]` -- SQL Server then reports "Invalid object name '@p0'",
    // found only by running this, not assumed. `topic` (the one genuine
    // VALUE, not an identifier) gets an explicit SqlParameter instead.
    public async Task NotifyAsync(string topic, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlRawAsync($@"
DECLARE @conversationHandle UNIQUEIDENTIFIER;
BEGIN DIALOG CONVERSATION @conversationHandle
    FROM SERVICE [{ServiceName}]
    TO SERVICE '{ServiceName}'
    ON CONTRACT [{ContractName}]
    WITH ENCRYPTION = OFF;
SEND ON CONVERSATION @conversationHandle
    MESSAGE TYPE [{MessageTypeName}] (@topic);
END CONVERSATION @conversationHandle;", [new SqlParameter("@topic", topic)], ct);

    // Same-service BEGIN DIALOG (FROM SERVICE X TO SERVICE X, since this
    // pass has only one queue/service for both ends) means EACH conversation
    // ending generates its own system "EndDialog" control message landing
    // in the SAME queue as the real WakeSignal message -- found only by
    // running this, not assumed: a naive single RECEIVE per call let a
    // leftover control message from a PRIOR notify's own END CONVERSATION
    // satisfy a LATER, unrelated wait instantly, with no real signal at
    // all. This loop RECEIVEs and inspects message_type_name, discarding
    // (ENDing) any non-WakeSignal control message and continuing to wait
    // out whatever time budget remains, so only a genuine WakeSignal
    // message ever counts as a wake.
    public async Task WaitForWakeAsync(string topic, TimeSpan maxWait, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
            await db.Database.OpenConnectionAsync(ct);
        try
        {
            var deadline = DateTimeOffset.UtcNow + maxWait;
            while (true)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return;

                Guid? conversationHandle = null;
                string? messageTypeName = null;
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"WAITFOR (RECEIVE TOP(1) conversation_handle, message_type_name FROM {QueueName}), TIMEOUT {(int)remaining.TotalMilliseconds};";
                    command.CommandTimeout = (int)remaining.TotalSeconds + 5;
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        conversationHandle = reader.GetGuid(0);
                        messageTypeName = reader.GetString(1);
                    }
                }

                if (conversationHandle is null)
                    return; // genuinely timed out with nothing in the queue at all

                await using var endCommand = connection.CreateCommand();
                endCommand.CommandText = "END CONVERSATION @handle;";
                endCommand.Parameters.Add(new SqlParameter("@handle", conversationHandle.Value));
                try
                {
                    await endCommand.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException)
                {
                    // Already ended (or ending) from the sender's own side --
                    // harmless; either way this conversation is done with.
                }

                if (messageTypeName == MessageTypeName)
                    return; // a genuine wake
                // else: a system control message (EndDialog/Error) from a
                // DIFFERENT, already-resolved conversation -- not a wake;
                // loop again with whatever time budget remains.
            }
        }
        finally
        {
            if (wasClosed)
                await db.Database.CloseConnectionAsync();
        }
    }
}
