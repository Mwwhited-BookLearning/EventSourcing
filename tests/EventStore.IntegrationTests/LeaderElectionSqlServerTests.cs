using EventStore.LeaderElection;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// [DoNotParallelize] -- isolates this class's tests from every other test
// in the run, not just from each other. MSTest's own method-level
// parallelism (MSTestSettings.cs) was starting many MsSqlContainers
// concurrently, causing real, repeatable Testcontainers readiness-check
// failures under the resulting resource contention (TODO.md's "SQL
// Server Testcontainers resource-exhaustion test flakiness" -- a
// baseline run failed 15 of 24 SqlServer classes before this fix).
[DoNotParallelize]
[TestClass]
public class LeaderElectionSqlServerTests
{
    private static MsSqlContainer _container = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _container.DisposeAsync();
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlServer(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        return new EventStoreContext(options, new SqlServerJsonPathTranslator());
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
