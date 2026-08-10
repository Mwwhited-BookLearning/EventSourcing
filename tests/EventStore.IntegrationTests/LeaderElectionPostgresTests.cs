using EventStore.LeaderElection;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class LeaderElectionPostgresTests
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
    public static async Task ClassCleanup()
    {
        await _container.DisposeAsync();
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
            .Options;
        return new EventStoreContext(options, new PostgresJsonPathTranslator());
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
