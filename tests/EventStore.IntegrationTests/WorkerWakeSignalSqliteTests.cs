using System.Diagnostics;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.WorkerWakeSignal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Push-notification wake-up layer for background workers" (docs/10-open-
// questions.md's resolved row) -- SqliteWorkerWakeSignal's own mechanics,
// exercised directly rather than only through a live RouterWorker, so a
// regression here is caught precisely rather than only as a vague full-
// suite timing flake. A distinct, GUID-based topic per test avoids
// cross-test contamination of the class's own static Channel/last-observed
// state (shared across every EventStoreContext instance in this process,
// by design -- see that class's own header comment).
[TestClass]
public class WorkerWakeSignalSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-wakesignal-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    [TestMethod]
    public async Task NotifyDuringAWaitWakesItWellBeforeTheTimeoutElapses()
    {
        var topic = $"topic-{Guid.NewGuid():N}";
        using var waiterDb = CreateContext();
        using var notifierDb = CreateContext();
        var waiter = new SqliteWorkerWakeSignal(waiterDb);
        var notifier = new SqliteWorkerWakeSignal(notifierDb);

        var stopwatch = Stopwatch.StartNew();
        var waitTask = waiter.WaitForWakeAsync(topic, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50); // let the wait actually start listening before signaling
        await notifier.NotifyAsync(topic, CancellationToken.None);
        await waitTask;
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"expected a near-immediate wake, took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public async Task WaitingWithNoSignalRunsTheFullTimeoutAsTheCorrectnessBackstop()
    {
        var topic = $"topic-{Guid.NewGuid():N}";
        using var db = CreateContext();
        var waiter = new SqliteWorkerWakeSignal(db);

        var stopwatch = Stopwatch.StartNew();
        await waiter.WaitForWakeAsync(topic, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250), $"expected the wait to run out its own timeout with no signal, took only {stopwatch.Elapsed}");
    }

    [TestMethod]
    public async Task ASignalThatHappenedBeforeAnyWaitIsStillObservedViaTheDurableMarker()
    {
        // Simulates a worker that starts waiting only AFTER a signal
        // already fired (e.g. a publish landing in the gap between
        // migration/startup and this worker's first WaitForWakeAsync
        // call) -- a fresh SqliteWorkerWakeSignal's own static
        // "last observed" state has never seen this topic before, so it
        // must fall back to the durable WakeSignal row, not just the
        // in-process Channel.
        var topic = $"topic-{Guid.NewGuid():N}";
        using var notifierDb = CreateContext();
        await new SqliteWorkerWakeSignal(notifierDb).NotifyAsync(topic, CancellationToken.None);

        using var waiterDb = CreateContext();
        var waiter = new SqliteWorkerWakeSignal(waiterDb);
        var stopwatch = Stopwatch.StartNew();
        await waiter.WaitForWakeAsync(topic, TimeSpan.FromSeconds(5), CancellationToken.None);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"expected an immediate wake from the durable marker, took {stopwatch.Elapsed}");
    }
}
