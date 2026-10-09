extern alias HostPostgresAssembly;

using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

// End-to-end over the real EventStore.Host.Postgres against a real Postgres
// container -- scenarios live in ProviderE2EScenarios, shared with SQL Server.
[TestClass]
[TestCategory("E2E")]
[TestProperty("Provider", "Postgres")]
[TestProperty("ADR", "095")]
public class ProviderE2EPostgresTests : ProviderE2EScenarios
{
    private static PostgreSqlContainer _container = default!;
    private static ProviderE2EHarness _harness = default!;

    protected override ProviderE2EHarness Harness => _harness;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseNpgsql(_container.GetConnectionString(), x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
            .Options;
        await using (var db = new EventStoreContext(options, new PostgresJsonPathTranslator()))
            await db.Database.MigrateAsync();
        _harness = await ProviderE2EHarness.StartAsync<HostPostgresAssembly::Program>("Postgres", _container.GetConnectionString());
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _harness.Dispose();
        await _container.DisposeAsync();
    }
}
