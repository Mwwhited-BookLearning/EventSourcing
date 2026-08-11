using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.ViewRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class ViewDefinitionPostgresTests
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
