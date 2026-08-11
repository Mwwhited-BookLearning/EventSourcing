using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EventStore.IntegrationTests;

[TestClass]
public class AttachmentPostgresTests
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
    public async Task AllAttachmentScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
        var attachments = new AttachmentService(db);

        await AttachmentScenarioAssertions.UploadingIdenticalBytesTwiceDeduplicatesToOneStoredObject(attachments, db);
        await AttachmentScenarioAssertions.LinkingTheSameAttachmentFromTwoDifferentEventsCreatesTwoAttachmentRefRows(attachments, db);
        await AttachmentScenarioAssertions.APublishCarryingAnAttachmentContentHashCreatesTheLinkThroughTheOrdinaryPublishPath(registry, publish, attachments, db);
        await AttachmentScenarioAssertions.AReaderLackingTheAttachmentsRequiredReadClaimIsForbidden(attachments);
        await AttachmentScenarioAssertions.ADirectRequiredReadClaimGovernsEvenWhenNoLinkExists(attachments);
        await AttachmentScenarioAssertions.ARequiredPublishClaimGatesReUploadOfAlreadyStoredBytesNotTheFirstUpload(attachments);
        await AttachmentScenarioAssertions.RetrievingAnUnknownContentHashReturnsNotFound(attachments);
    }
}
