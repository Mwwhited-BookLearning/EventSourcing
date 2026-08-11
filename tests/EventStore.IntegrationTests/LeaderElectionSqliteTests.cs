using EventStore.LeaderElection;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class LeaderElectionSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-leader-election-{Guid.NewGuid():N}.db");
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
    public async Task AllLeaderElectionScenarios()
    {
        using var db = CreateContext();
        var leaderElection = new LeaderElectionService(db);

        await LeaderElectionScenarioAssertions.FirstAcquireForANewRoleCreatesTheLeaseRowAndSucceeds(leaderElection);
        await LeaderElectionScenarioAssertions.ASecondDifferentHolderCannotAcquireAStillValidLease(leaderElection);
        await LeaderElectionScenarioAssertions.TheOriginalHolderCanRenewItsOwnStillValidLease(leaderElection);
        await LeaderElectionScenarioAssertions.AnotherInstanceCanClaimTheLeaseOnceItExpires(leaderElection);
        await LeaderElectionScenarioAssertions.TwoDifferentWorkerRolesHoldAndLoseTheirLeasesCompletelyIndependently(leaderElection);
        await LeaderElectionScenarioAssertions.TwoUncoordinatedContextsWithNoMutualExclusionCanSilentlyLoseOneUpdate(CreateContext);
    }
}
