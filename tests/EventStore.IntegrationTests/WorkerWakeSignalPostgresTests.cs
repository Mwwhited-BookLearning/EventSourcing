using System.Diagnostics;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

// "Push-notification wake-up layer for background workers" -- Postgres'
// own real LISTEN/NOTIFY mechanism, against a real container, not mocked.
[TestClass]
public class WorkerWakeSignalPostgresTests
{
    private static PostgreSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup() => await _container.DisposeAsync();

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
            .Options;
        return new EventStoreContext(options, new PostgresJsonPathTranslator());
    }

    [TestMethod]
    public async Task NotifyDuringAWaitWakesItWellBeforeTheTimeoutElapsesOverARealListenNotifyConnection()
    {
        var topic = $"topic_{Guid.NewGuid():N}";
        using var waiterDb = CreateContext();
        using var notifierDb = CreateContext();
        var waiter = new PostgresWorkerWakeSignal(waiterDb);
        var notifier = new PostgresWorkerWakeSignal(notifierDb);

        var stopwatch = Stopwatch.StartNew();
        var waitTask = waiter.WaitForWakeAsync(topic, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(300); // let the dedicated LISTEN connection actually open and issue LISTEN before signaling
        await notifier.NotifyAsync(topic, CancellationToken.None);
        await waitTask;
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"expected a near-immediate NOTIFY-driven wake, took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public async Task WaitingWithNoNotifyRunsTheFullTimeoutAsTheCorrectnessBackstop()
    {
        var topic = $"topic_{Guid.NewGuid():N}";
        using var db = CreateContext();
        var waiter = new PostgresWorkerWakeSignal(db);

        var stopwatch = Stopwatch.StartNew();
        await waiter.WaitForWakeAsync(topic, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(400), $"expected the wait to run out its own timeout with no NOTIFY, took only {stopwatch.Elapsed}");
    }
}
