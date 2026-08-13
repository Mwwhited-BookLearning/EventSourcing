using System.Diagnostics;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// "Push-notification wake-up layer for background workers" -- SQL Server's
// own real Service Broker mechanism (queue/service/contract/message type
// created by this project's own AddWorkerWakeSignal migration), against a
// real container, not mocked. Both scenarios live in ONE test method,
// deliberately -- MSTest's own method-level parallelism would otherwise let
// them race against the SAME shared queue (this class's own header comment
// on SqlServerWorkerWakeSignal: one queue, no topic filtering, scoped to
// this single pass), the identical "one combined scenario method"
// discipline every other multi-scenario *Tests.cs file in this suite
// already uses for exactly this reason.
// [DoNotParallelize] -- isolates this class's tests from every other test
// in the run, not just from each other. MSTest's own method-level
// parallelism (MSTestSettings.cs) was starting many MsSqlContainers
// concurrently, causing real, repeatable Testcontainers readiness-check
// failures under the resulting resource contention (TODO.md's "SQL
// Server Testcontainers resource-exhaustion test flakiness" -- a
// baseline run failed 15 of 24 SqlServer classes before this fix).
[DoNotParallelize]
[TestClass]
public class WorkerWakeSignalSqlServerTests
{
    private static MsSqlContainer _container = default!;
    private static string _connectionString = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new MsSqlBuilder().Build();
        await _container.StartAsync();

        // ADR-095's own SQL Server ENABLE_BROKER statement fails outright
        // against Testcontainers' own default connection ("master"): SQL
        // Server refuses "Option 'ENABLE_BROKER' cannot be set in database
        // 'master'" -- found only by actually running this, not assumed. A
        // real Aspire-provisioned deployment already connects to its own
        // named database (AppHost.cs's own AddDatabase), never master, so
        // this test creates one too, matching that same real shape rather
        // than papering over a Testcontainers-only default.
        const string databaseName = "WorkerWakeSignalTest";
        await using (var masterConnection = new SqlConnection(_container.GetConnectionString()))
        {
            await masterConnection.OpenAsync();
            await using var command = masterConnection.CreateCommand();
            command.CommandText = $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{databaseName}') CREATE DATABASE [{databaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = databaseName };
        _connectionString = builder.ConnectionString;

        using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup() => await _container.DisposeAsync();

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlServer(_connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        return new EventStoreContext(options, new SqlServerJsonPathTranslator());
    }

    [TestMethod]
    public async Task AllWorkerWakeSignalScenarios()
    {
        using var waiterDb = CreateContext();
        using var notifierDb = CreateContext();
        var waiter = new SqlServerWorkerWakeSignal(waiterDb);
        var notifier = new SqlServerWorkerWakeSignal(notifierDb);

        var stopwatch = Stopwatch.StartNew();
        var waitTask = waiter.WaitForWakeAsync("router", TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(300); // let the WAITFOR (RECEIVE ...) call actually start blocking before sending
        await notifier.NotifyAsync("router", CancellationToken.None);
        await waitTask;
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"expected a near-immediate Service Broker-driven wake, took {stopwatch.Elapsed}");

        // The message the scenario above sent was RECEIVEd (consumed) by
        // that same wait -- the queue is genuinely empty again here, so
        // this second scenario's own timeout-with-nothing-to-receive case
        // is real, not accidentally satisfied by a leftover message.
        var secondStopwatch = Stopwatch.StartNew();
        await waiter.WaitForWakeAsync("router", TimeSpan.FromMilliseconds(500), CancellationToken.None);
        secondStopwatch.Stop();
        Assert.IsTrue(secondStopwatch.Elapsed >= TimeSpan.FromMilliseconds(400), $"expected the wait to run out its own timeout with no message, took only {secondStopwatch.Elapsed}");

        // ExtendWorkerWakeSignalPerTopic -- a non-"router" topic gets its
        // OWN queue/service/contract/message-type set, exercised here for
        // real against the same container, not assumed from "router"'s own
        // scenario above.
        var derivationStopwatch = Stopwatch.StartNew();
        var derivationWait = waiter.WaitForWakeAsync("derivation", TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(300);
        await notifier.NotifyAsync("derivation", CancellationToken.None);
        await derivationWait;
        derivationStopwatch.Stop();
        Assert.IsTrue(derivationStopwatch.Elapsed < TimeSpan.FromSeconds(3), $"expected derivation's own per-topic queue to wake near-immediately, took {derivationStopwatch.Elapsed}");

        // The real bug ADR-095's own Consequences named as the reason this
        // migration exists: before per-topic queues, ANY message on the one
        // shared queue satisfied whichever topic happened to be waiting,
        // regardless of which topic actually notified. Two DIFFERENT topics
        // waiting concurrently, only one notified -- only that one may wake;
        // the other must still run out its own full timeout. Two separate
        // DbContexts/connections for the two concurrent waits -- one
        // connection running two concurrent WAITFOR (RECEIVE ...) calls
        // needs MultipleActiveResultSets, which this test's own connection
        // string doesn't enable (found only by running this).
        using var peerSyncWaiterDb = CreateContext();
        var peerSyncWaiter = new SqlServerWorkerWakeSignal(peerSyncWaiterDb);
        var isolationStopwatch = Stopwatch.StartNew();
        var peerSyncWait = peerSyncWaiter.WaitForWakeAsync("peersync", TimeSpan.FromMilliseconds(800), CancellationToken.None);
        var expectedResponseWait = waiter.WaitForWakeAsync("expectedresponse", TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(300);
        await notifier.NotifyAsync("expectedresponse", CancellationToken.None);
        await Task.WhenAll(peerSyncWait, expectedResponseWait);
        isolationStopwatch.Stop();
        Assert.IsTrue(isolationStopwatch.Elapsed >= TimeSpan.FromMilliseconds(700),
            $"expected peersync's own wait to run out its full timeout, unaffected by expectedresponse's own notify on a DIFFERENT topic's queue, took only {isolationStopwatch.Elapsed}");
    }
}
