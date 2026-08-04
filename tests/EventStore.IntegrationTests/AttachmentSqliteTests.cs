using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

[TestClass]
public class AttachmentSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-attachments-{Guid.NewGuid():N}.db");
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
    public async Task AllAttachmentScenarios()
    {
        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
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
