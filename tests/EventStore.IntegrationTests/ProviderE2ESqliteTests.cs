using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// End-to-end over the real EventStore.Host.Sqlite against a real SQLite file -- the same
// scenarios as ProviderE2EPostgresTests / ProviderE2ESqlServerTests (shared in ProviderE2EScenarios).
[TestClass]
[TestCategory("E2E")]
[TestProperty("Provider", "Sqlite")]
[TestProperty("ADR", "095")]
public class ProviderE2ESqliteTests : ProviderE2EScenarios
{
    private static string _dbPath = default!;
    private static ProviderE2EHarness _harness = default!;

    protected override ProviderE2EHarness Harness => _harness;

    protected override async Task BurnSequenceNumbersAsync(int count)
    {
        // AUTOINCREMENT tracks the high-water mark in sqlite_sequence; bump it without inserting rows.
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sqlite_sequence SET seq = seq + $n WHERE name = 'Events'; " +
                              "INSERT INTO sqlite_sequence (name, seq) SELECT 'Events', $n WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = 'Events')";
        command.Parameters.AddWithValue("$n", count);
        await command.ExecuteNonQueryAsync();
    }

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-e2e-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();
        _harness = await ProviderE2EHarness.StartAsync<Program>("Sqlite", $"Data Source={_dbPath}");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _harness.Dispose();
        SqliteConnection.ClearAllPools();
        TempDbFile.Delete(_dbPath);
    }
}
