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
public class MeridianWorkflowASqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-meridian-workflow-a-{Guid.NewGuid():N}.db");
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
    public async Task AllMeridianWorkflowAScenarios()
    {
        using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), cache, UpcastingTestSupport.CreateEvaluator());
        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector());
        var attachments = new AttachmentService(db);

        await MeridianWorkflowAScenarioAssertions.UploadingAPassportScanAndLinkingItToTheApplicantBothGenerallyAndToThisEvent(registry, publish, attachments, db);
        await MeridianWorkflowAScenarioAssertions.AProofOfAddressLetterIsUploadedAndLinkedTheSameWayAsASecondDocumentType(registry, publish, attachments);
        await MeridianWorkflowAScenarioAssertions.AConfidentLivenessResultIsCapturedAsAcceptedAndFoldsImmediately(registry, publish, db);
        await MeridianWorkflowAScenarioAssertions.AnInconclusiveLivenessResultIsCapturedAsPendingReviewViaTheExplicitReviewPendingMarker(registry, publish, db);
        await MeridianWorkflowAScenarioAssertions.AnAnalystsAuthorityDecisionResolvesAnInconclusiveLivenessCaptureAndTheAuthoritativeEntityStoreCatchesUp(registry, publish, db);
        await MeridianWorkflowAScenarioAssertions.DocumentsAndBiometricResultAreBothVisibleToAnAnalystBeforeTheIdentityClaimIsEvenSubmitted(registry, publish, attachments, db);
        await MeridianWorkflowAScenarioAssertions.AnApplicantSelfAttestsAndTheClaimLandsUnattestedPersistedImmediately(registry, publish, db);
        await MeridianWorkflowAScenarioAssertions.AnAnalystLackingTheIdentityReviewClaimCannotPublishAnAuthorityDecision(registry, publish, db);
        await MeridianWorkflowAScenarioAssertions.AnAnalystHoldingIdentityReviewAcceptsTheClaimAndTheAuthoritativeEntityStoreNowFoldsIt(registry, publish, attachments, db);
        await MeridianWorkflowAScenarioAssertions.AnAnalystHoldingIdentityReviewRejectsTheClaimInsteadAndTheEntityStoreNeverReflectsIt(registry, publish, db);
    }
}
