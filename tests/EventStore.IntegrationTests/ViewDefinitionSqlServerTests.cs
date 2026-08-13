using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.ViewRegistry;
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
public class ViewDefinitionSqlServerTests
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
    public async Task AllViewDefinitionScenarios()
    {
        using var db = CreateContext();
        var registry = new ViewDefinitionService(db);

        await ViewDefinitionScenarioAssertions.RegisteringAViewDefinitionMakesItTheActiveOneForItsEntityTypeAndViewKind(registry);
        await ViewDefinitionScenarioAssertions.RegisteringANewVersionDeprecatesThePriorOneButBothStillExist(registry);
        await ViewDefinitionScenarioAssertions.ASchemaVersionTheActiveTemplateDoesNotDeclareCompatibilityWithFallsBackToTheDeprecatedButCompatibleVersion(registry);
        await ViewDefinitionScenarioAssertions.AnEntityTypeWithNothingRegisteredReturnsNullTheClientsOwnSignalToRenderTheGenericFallback(registry);
        await ViewDefinitionScenarioAssertions.RegistrationValidatesViewKindTemplateContentAndCompatibleSchemaVersions(registry);
        await ViewDefinitionScenarioAssertions.ATemplateContainingAHardcodedLiteralInsteadOfATranslationKeyIsRejected(registry);
    }
}
