using System.Text;
using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Domain.AccessLog;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Event Log/AccessLog Archival Segment Detachment"
// (docs/08-build-plan.md, ADR-089). Exercises ArchivalService directly
// against a real IAttachmentContentStore (InMemoryAttachmentContentStore
// -- the one registered backend this build stage ships, EventStore.
// Attachments), the same "exercise the mechanics directly" pattern every
// other item in this build stage already uses.
internal static class ArchivalScenarioAssertions
{
    private static Task RegisterType(SchemaRegistryService registry, string appId, string typeName) =>
        registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

    public static async Task ArchivingAVerifiedSegmentMovesItToTheContentStoreAndLeavesACorrectCheckpoint(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival)
    {
        const string appId = "archival-demo-1";
        await RegisterType(registry, appId, "ChainedType");

        var e1 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var e3 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 3 }""", null, null), TestClaimsPrincipal.None);
        var lastEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == e3.CorrelationId);

        var result = await archival.ArchiveEventLogSegmentAsync(e3.SequenceNumber, "in-memory");

        var archived = (ArchiveResult.Archived)result;
        // 1, not e1.SequenceNumber -- SchemaRegistryService.RegisterAsync's
        // own SchemaRegistered audit event (ADR-067) already consumed
        // SequenceNumber 1 for this brand-new AppId before e1 ever
        // published, so the TRUE first row in this segment is that audit
        // event, not e1 itself; the checkpoint's own range must still
        // start at 1 -- nothing in the primary table before it.
        Assert.AreEqual(1L, archived.Checkpoint.SequenceNumberRangeStart, "no prior checkpoint exists yet -- the very first archival always starts at SequenceNumber 1");
        Assert.AreEqual(e3.SequenceNumber, archived.Checkpoint.SequenceNumberRangeEnd);
        Assert.AreEqual(lastEvent.ChainHash, archived.Checkpoint.ChainHashAtRangeEnd);
        Assert.AreEqual("in-memory", archived.Checkpoint.ContentProviderKey);

        // "moves it" -- the archived rows are actually gone from the
        // primary table, not just copied.
        var remaining = await db.Events.CountAsync(e => e.AppId == appId);
        Assert.AreEqual(0, remaining, "the archived segment must be detached from the primary table, not merely duplicated into the content store");

        var savedCheckpoint = await db.EventLogChainCheckpoints.AsNoTracking().SingleAsync(c => c.Id == archived.Checkpoint.Id);
        Assert.AreEqual(archived.Checkpoint.ContentProviderRef, savedCheckpoint.ContentProviderRef);
    }

    public static async Task LiveVerificationAfterAnArchivalVerifiesOnlyTheStillLivePortionStartingFromTheCheckpoint(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival, ChainVerificationService verifier)
    {
        const string appId = "archival-demo-2";
        await RegisterType(registry, appId, "ChainedType");

        var e1 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        await archival.ArchiveEventLogSegmentAsync(e2.SequenceNumber, "in-memory");

        // Published AFTER the archival -- this event's own ChainHash was
        // computed (at publish time, by EventAppender) chaining from e2's
        // real ChainHash, exactly as if e2 had never been archived.
        var e3 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 3 }""", null, null), TestClaimsPrincipal.None);

        var result = await verifier.VerifyAsync(e3.SequenceNumber);

        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(result);
        // Only e3 is left live (e1/e2 were detached) -- proves this
        // verification pass never needed to read the archived segment at
        // all: there is nothing left in the primary table for it to read.
        Assert.AreEqual(1, ((ChainVerificationResult.Verified)result).EventCount);
    }

    public static async Task RetrievingAnArchivedSegmentAndReVerifyingItsOwnInternalChainConfirmsItsUnaltered(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival)
    {
        const string appId = "archival-demo-3";
        await RegisterType(registry, appId, "ChainedType");

        await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var archived = (ArchiveResult.Archived)await archival.ArchiveEventLogSegmentAsync(e2.SequenceNumber, "in-memory");

        var result = await archival.ReVerifyEventLogSegmentAsync(archived.Checkpoint);

        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(result);
        // Derived from the checkpoint's OWN recorded range, not a literal
        // count -- archival operates on the Event Log's one global
        // SequenceNumber space (ADR-089 never scopes it per-AppId), so
        // running this scenario after an earlier one (which already
        // archived through some prior boundary) legitimately archives
        // fewer rows here than "from SequenceNumber 1" would; the
        // self-consistent invariant is range width, not an absolute count.
        var expectedCount = archived.Checkpoint.SequenceNumberRangeEnd - archived.Checkpoint.SequenceNumberRangeStart + 1;
        Assert.AreEqual((int)expectedCount, ((ChainVerificationResult.Verified)result).EventCount);
    }

    public static async Task ATamperedArchivedBlobIsDetectedOnReVerification(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival, IAttachmentContentStore contentStore)
    {
        const string appId = "archival-demo-4";
        await RegisterType(registry, appId, "ChainedType");

        await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var archived = (ArchiveResult.Archived)await archival.ArchiveEventLogSegmentAsync(e2.SequenceNumber, "in-memory");

        // Test-only direct tamper of the archived BLOB itself (simulating
        // corruption in the content-store backend) -- round-trips through
        // the real ArchivedEventLogBundle types (not raw string surgery
        // against the JSON text, which is fragile against exactly how
        // System.Text.Json happens to format/escape the embedded Payload
        // string) to swap the first line's own Payload for a different,
        // still-well-formed one.
        var bytes = await contentStore.RetrieveAsync(archived.Checkpoint.ContentProviderRef);
        var bundle = ArchivedEventLogBundle.ParseNdjson(Encoding.UTF8.GetString(bytes));
        bundle.Lines[0].Event.Payload = """{ "Amount": 999999 }""";
        var tamperedRef = await contentStore.StoreAsync(Encoding.UTF8.GetBytes(bundle.ToNdjson()));
        var tamperedCheckpoint = new EventStore.Domain.EventLog.ChainCheckpoint
        {
            SequenceNumberRangeStart = archived.Checkpoint.SequenceNumberRangeStart,
            SequenceNumberRangeEnd = archived.Checkpoint.SequenceNumberRangeEnd,
            ChainHashAtRangeEnd = archived.Checkpoint.ChainHashAtRangeEnd,
            ContentProviderKey = archived.Checkpoint.ContentProviderKey,
            ContentProviderRef = tamperedRef,
        };

        var result = await archival.ReVerifyEventLogSegmentAsync(tamperedCheckpoint);

        Assert.IsInstanceOfType<ChainVerificationResult.Tampered>(result);
    }

    public static async Task ArchivingASecondSegmentChainsFromThePriorCheckpointNotFromGenesis(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival, ChainVerificationService verifier)
    {
        const string appId = "archival-demo-5";
        await RegisterType(registry, appId, "ChainedType");

        var e1 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var first = (ArchiveResult.Archived)await archival.ArchiveEventLogSegmentAsync(e1.SequenceNumber, "in-memory");

        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var e3 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 3 }""", null, null), TestClaimsPrincipal.None);
        var second = (ArchiveResult.Archived)await archival.ArchiveEventLogSegmentAsync(e3.SequenceNumber, "in-memory");

        Assert.AreEqual(e2.SequenceNumber, second.Checkpoint.SequenceNumberRangeStart, "the second archival's own range picks up exactly where the first left off");

        // The second segment's own events were originally chained (at
        // publish time) from the FIRST checkpoint's own boundary hash, not
        // Genesis -- re-verification must seed from that same predecessor
        // to land on the correct expected value, proving it isn't silently
        // assuming Genesis for every archived segment.
        var reVerified = await archival.ReVerifyEventLogSegmentAsync(second.Checkpoint);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(reVerified);

        // Live verification through e3 still succeeds even though NOTHING
        // is left in the primary table except whatever's still live.
        var liveResult = await verifier.VerifyAsync(e3.SequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(liveResult);
        Assert.AreEqual(0, await db.Events.CountAsync(e => e.AppId == appId));
    }

    public static async Task ArchivingAnAlreadyTamperedLiveSegmentIsRefusedAndNothingIsDetachedOrCheckpointed(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival)
    {
        const string appId = "archival-demo-6";
        await RegisterType(registry, appId, "ChainedType");

        await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        var e2 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", null, null), TestClaimsPrincipal.None);
        var countBeforeArchival = await db.Events.CountAsync(e => e.AppId == appId);
        var checkpointCountBeforeArchival = await db.EventLogChainCheckpoints.CountAsync();

        var row = await db.Events.SingleAsync(e => e.EventId == e2.CorrelationId);
        var originalPayload = row.Payload;
        row.Payload = """{ "Amount": 999999 }"""; // direct DB edit -- PayloadHash deliberately left stale
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        try
        {
            var result = await archival.ArchiveEventLogSegmentAsync(e2.SequenceNumber, "in-memory");

            Assert.IsInstanceOfType<ArchiveResult.SegmentNotVerified>(result);
            Assert.AreEqual(countBeforeArchival, await db.Events.CountAsync(e => e.AppId == appId), "a refused archival must leave every row exactly where it was");
            Assert.AreEqual(checkpointCountBeforeArchival, await db.EventLogChainCheckpoints.CountAsync(), "a refused archival must never create a checkpoint either");
        }
        finally
        {
            // This scenario shares one EventStoreContext with every other
            // scenario in the same test method -- leaving this row tampered
            // would permanently poison every LATER scenario's own live
            // verification (anything covering this SequenceNumber would
            // fail forever after, not just here), the exact same "give
            // every scenario its own clean state" concern this repo's
            // tests already apply via unique AppIds, extended to a
            // deliberate in-place edit that must be undone once this
            // scenario's own assertion is done with it.
            var tamperedRow = await db.Events.SingleAsync(e => e.EventId == e2.CorrelationId);
            tamperedRow.Payload = originalPayload;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    public static async Task ArchivingWithNothingNewSinceThePriorCheckpointIsANoOp(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival)
    {
        const string appId = "archival-demo-7";
        await RegisterType(registry, appId, "ChainedType");

        var e1 = (PublishResult.Accepted)await publish.PublishAsync("ChainedType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        await archival.ArchiveEventLogSegmentAsync(e1.SequenceNumber, "in-memory");
        var checkpointCountAfterFirstArchival = await db.EventLogChainCheckpoints.CountAsync();

        var result = await archival.ArchiveEventLogSegmentAsync(e1.SequenceNumber, "in-memory");

        Assert.IsInstanceOfType<ArchiveResult.NothingToArchive>(result);
        Assert.AreEqual(checkpointCountAfterFirstArchival, await db.EventLogChainCheckpoints.CountAsync(), "a no-op archival attempt must never create a second, redundant checkpoint");
    }

    // AccessLog's own independent chain -- same mechanism, genuinely
    // separate checkpoint table, confirmed not to share or collide with
    // the Event Log's own (ADR-089's own explicit exit criterion).
    public static async Task AccessLogArchivesAndReVerifiesIndependentlyWithItsOwnDistinctCheckpointRow(
        EventStoreContext db, ArchivalService archival, AccessLogChainVerificationService accessLogVerifier)
    {
        var eventLogCheckpointCountBefore = await db.EventLogChainCheckpoints.CountAsync();
        var accessLogCheckpointCountBefore = await db.AccessLogChainCheckpoints.CountAsync();
        var accessLogEntryCountBefore = await db.AccessLogEntries.CountAsync();

        await AccessLogAppender.AppendAsync(db, "reader-1", "Authoritative", null, "Authoritative", "resource-1", "query");
        await AccessLogAppender.AppendAsync(db, "reader-1", "Authoritative", null, "Authoritative", "resource-2", "query");
        var entry2 = await db.AccessLogEntries.AsNoTracking().OrderByDescending(e => e.SequenceNumber).FirstAsync();

        var archived = (ArchiveResult.Archived)await archival.ArchiveAccessLogSegmentAsync(entry2.SequenceNumber, "in-memory");

        Assert.AreEqual(entry2.ChainHash, archived.Checkpoint.ChainHashAtRangeEnd);
        Assert.AreEqual(accessLogEntryCountBefore, await db.AccessLogEntries.CountAsync(), "the two entries just appended and archived above must be detached again");
        Assert.AreEqual(eventLogCheckpointCountBefore, await db.EventLogChainCheckpoints.CountAsync(), "AccessLog's own checkpoint must never land in the Event Log's own checkpoint table");
        Assert.AreEqual(accessLogCheckpointCountBefore + 1, await db.AccessLogChainCheckpoints.CountAsync());

        var reVerified = await archival.ReVerifyAccessLogSegmentAsync(archived.Checkpoint);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(reVerified);

        var liveResult = await accessLogVerifier.VerifyAsync(entry2.SequenceNumber);
        Assert.IsInstanceOfType<ChainVerificationResult.Verified>(liveResult);
        Assert.AreEqual(0, ((ChainVerificationResult.Verified)liveResult).EventCount, "nothing is live past the checkpoint -- everything archived");
    }

    // A still-live child's own reference to an archived parent is left
    // exactly as-is (a dangling reference this design already tolerates
    // via ADR-005's Permissive mode), never silently deleted alongside
    // the archived parent's own row.
    public static async Task ALiveChildsReferenceToAnArchivedParentSurvivesArchivalAsAToleratedDanglingReference(
        SchemaRegistryService registry, PublishService publish, EventStoreContext db, ArchivalService archival)
    {
        const string appId = "archival-demo-8";
        await registry.RegisterAsync("PermissiveType", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: "Permissive", RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var parent = (PublishResult.Accepted)await publish.PublishAsync("PermissiveType", new PublishEventRequest(appId, 1, """{ "Amount": 1 }""", null, null), TestClaimsPrincipal.None);
        await archival.ArchiveEventLogSegmentAsync(parent.SequenceNumber, "in-memory");

        var child = (PublishResult.Accepted)await publish.PublishAsync(
            "PermissiveType", new PublishEventRequest(appId, 1, """{ "Amount": 2 }""", [parent.CorrelationId], null), TestClaimsPrincipal.None);

        var survivingLink = await db.EventParents.AsNoTracking().SingleOrDefaultAsync(p => p.ChildEventId == child.CorrelationId && p.ParentEventId == parent.CorrelationId);
        Assert.IsNotNull(survivingLink, "the live child's own lineage record must survive its parent's archival, even though the parent row itself is gone");
        Assert.IsFalse(await db.Events.AnyAsync(e => e.EventId == parent.CorrelationId), "the archived parent's own StoredEvent row is genuinely gone");
    }
}
