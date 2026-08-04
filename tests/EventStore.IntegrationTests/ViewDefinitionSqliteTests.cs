using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.ViewRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class ViewDefinitionSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-viewdef-{Guid.NewGuid():N}.db");
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
    public async Task AllViewDefinitionScenarios()
    {
        using var db = CreateContext();
        var registry = new ViewDefinitionService(db);

        await ViewDefinitionScenarioAssertions.RegisteringAViewDefinitionMakesItTheActiveOneForItsEntityTypeAndViewKind(registry);
        await ViewDefinitionScenarioAssertions.RegisteringANewVersionDeprecatesThePriorOneButBothStillExist(registry);
        await ViewDefinitionScenarioAssertions.ASchemaVersionTheActiveTemplateDoesNotDeclareCompatibilityWithFallsBackToTheDeprecatedButCompatibleVersion(registry);
        await ViewDefinitionScenarioAssertions.AnEntityTypeWithNothingRegisteredReturnsNullTheClientsOwnSignalToRenderTheGenericFallback(registry);
        await ViewDefinitionScenarioAssertions.RegistrationValidatesViewKindTemplateContentAndCompatibleSchemaVersions(registry);
    }
}
