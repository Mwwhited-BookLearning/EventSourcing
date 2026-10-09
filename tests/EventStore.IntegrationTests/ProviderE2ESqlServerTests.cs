extern alias HostSqlServerAssembly;

using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EventStore.IntegrationTests;

// End-to-end over the real EventStore.Host.SqlServer against a real SQL
// Server container -- scenarios live in ProviderE2EScenarios, shared with
// Postgres. [DoNotParallelize]: same MsSqlContainer resource-exhaustion
// flakiness the other SqlServer classes document.
[DoNotParallelize]
[TestClass]
[TestCategory("E2E")]
[TestProperty("Provider", "SqlServer")]
[TestProperty("ADR", "095")]
public class ProviderE2ESqlServerTests : ProviderE2EScenarios
{
    private static MsSqlContainer _container = default!;
    private static ProviderE2EHarness _harness = default!;

    private static string _connectionString = default!;

    protected override ProviderE2EHarness Harness => _harness;

    // Reseeding the identity jumps SequenceNumber forward, the same visible effect as identity
    // values consumed by rolled-back inserts.
    protected override async Task BurnSequenceNumbersAsync(int count)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DECLARE @m bigint = ISNULL((SELECT MAX(SequenceNumber) FROM Events), 0); DECLARE @n bigint = @m + {count}; DBCC CHECKIDENT ('Events', RESEED, @n) WITH NO_INFOMSGS;";
        await command.ExecuteNonQueryAsync();
    }

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        // ENABLE_BROKER (ADR-095's Service Broker wake queue) cannot be set in "master".
        const string databaseName = "ProviderE2ETest";
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            command.CommandText = $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{databaseName}') CREATE DATABASE [{databaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;
        _connectionString = connectionString;

        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqlServerJsonPathTranslator()))
            await db.Database.MigrateAsync();
        _harness = await ProviderE2EHarness.StartAsync<HostSqlServerAssembly::Program>("SqlServer", connectionString);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _harness.Dispose();
        await _container.DisposeAsync();
    }
}
