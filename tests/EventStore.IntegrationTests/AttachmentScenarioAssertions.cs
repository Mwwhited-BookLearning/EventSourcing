using EventStore.Attachments;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Binary Attachments" (docs/08-build-plan.md, ADR-032).
// The GraphQL-browse exit criterion is deferred to be re-verified once
// "GraphQL-Only Query Layer" lands (that item's own build-plan section
// already flags this forward dependency) -- not covered here.
internal static class AttachmentScenarioAssertions
{
    public static async Task UploadingIdenticalBytesTwiceDeduplicatesToOneStoredObject(AttachmentService service, EventStoreContext db)
    {
        var bytes = "attachment-demo-1 content"u8.ToArray();

        var first = await service.UploadAsync(bytes, "text/plain", "notes.txt", null, null, TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<UploadAttachmentResult.Created>(first);
        var contentHash = ((UploadAttachmentResult.Created)first).ContentHash;

        var second = await service.UploadAsync(bytes, "text/plain", "notes.txt", null, null, TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<UploadAttachmentResult.Deduplicated>(second);
        Assert.AreEqual(contentHash, ((UploadAttachmentResult.Deduplicated)second).ContentHash);

        Assert.AreEqual(1, await db.Attachments.CountAsync(a => a.ContentHash == contentHash), "identical bytes must be stored exactly once");
    }

    public static async Task LinkingTheSameAttachmentFromTwoDifferentEventsCreatesTwoAttachmentRefRows(AttachmentService service, EventStoreContext db)
    {
        var bytes = "attachment-demo-2 content"u8.ToArray();
        var created = (UploadAttachmentResult.Created)await service.UploadAsync(bytes, "text/plain", null, null, null, TestClaimsPrincipal.None);

        await service.LinkAsync(created.ContentHash, entityId: "patient:2a", eventId: null);
        await service.LinkAsync(created.ContentHash, entityId: "patient:2b", eventId: null);

        var refs = await db.AttachmentRefs.AsNoTracking().Where(r => r.ContentHash == created.ContentHash).ToListAsync();
        Assert.AreEqual(2, refs.Count, "deduplication makes an attachment many-to-many with entities/events by construction");
        Assert.IsTrue(refs.Any(r => r.EntityId == "patient:2a"));
        Assert.IsTrue(refs.Any(r => r.EntityId == "patient:2b"));
    }

    public static async Task APublishCarryingAnAttachmentContentHashCreatesTheLinkThroughTheOrdinaryPublishPath(
        SchemaRegistryService schemaRegistry, PublishService publish, AttachmentService attachments, EventStoreContext db)
    {
        const string appId = "attachment-demo-3";
        await schemaRegistry.RegisterAsync("ClaimSubmitted", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Note": { "type": "string" } }, "required": ["Note"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: null,
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var uploaded = (UploadAttachmentResult.Created)await attachments.UploadAsync(
            "attachment-demo-3 scanned form"u8.ToArray(), "application/pdf", "form.pdf", null, null, TestClaimsPrincipal.None);

        var result = await publish.PublishAsync("ClaimSubmitted",
            new PublishEventRequest(appId, 1, """{ "Note": "supporting document attached" }""", null, null, null, null, [uploaded.ContentHash]),
            TestClaimsPrincipal.None);

        Assert.IsInstanceOfType<PublishResult.Accepted>(result);
        var accepted = (PublishResult.Accepted)result;

        var linked = await db.AttachmentRefs.AsNoTracking().SingleAsync(r => r.ContentHash == uploaded.ContentHash && r.EventId == accepted.CorrelationId);
        Assert.AreEqual(accepted.CorrelationId, linked.EventId);
    }

    public static async Task AReaderLackingTheAttachmentsRequiredReadClaimIsForbidden(AttachmentService service)
    {
        var created = (UploadAttachmentResult.Created)await service.UploadAsync(
            "attachment-demo-4 sensitive content"u8.ToArray(), "text/plain", null, "clinical:full-record", null, TestClaimsPrincipal.None);

        var forbidden = await service.RetrieveAsync(created.ContentHash, TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<RetrieveAttachmentResult.Forbidden>(forbidden);

        var allowed = await service.RetrieveAsync(created.ContentHash, TestClaimsPrincipal.With("clinical:full-record"));
        Assert.IsInstanceOfType<RetrieveAttachmentResult.Found>(allowed);
    }

    public static async Task ADirectRequiredReadClaimGovernsEvenWhenNoLinkExists(AttachmentService service)
    {
        // ADR-032 -- a standalone attachment (no EntityId/EventId link at
        // all) can still carry its own direct claim; there is nothing to
        // "inherit from," which is exactly the point of a direct claim.
        var created = (UploadAttachmentResult.Created)await service.UploadAsync(
            "attachment-demo-5 work guide"u8.ToArray(), "application/pdf", "guide.pdf", "docs:internal", null, TestClaimsPrincipal.None);

        var result = await service.RetrieveAsync(created.ContentHash, TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<RetrieveAttachmentResult.Forbidden>(result);
    }

    public static async Task ARequiredPublishClaimGatesReUploadOfAlreadyStoredBytesNotTheFirstUpload(AttachmentService service)
    {
        var bytes = "attachment-demo-6 content"u8.ToArray();

        var first = await service.UploadAsync(bytes, "text/plain", null, null, "clinical:full-record", TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<UploadAttachmentResult.Created>(first, "the first upload creates the row -- nothing to check a claim against yet");

        var reupload = await service.UploadAsync(bytes, "text/plain", null, null, "clinical:full-record", TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<UploadAttachmentResult.Forbidden>(reupload, "a caller lacking the claim must not be able to re-assert already-stored bytes");

        var withClaim = await service.UploadAsync(bytes, "text/plain", null, null, "clinical:full-record", TestClaimsPrincipal.With("clinical:full-record"));
        Assert.IsInstanceOfType<UploadAttachmentResult.Deduplicated>(withClaim);
    }

    public static async Task RetrievingAnUnknownContentHashReturnsNotFound(AttachmentService service)
    {
        var result = await service.RetrieveAsync("0000000000000000000000000000000000000000000000000000000000000000", TestClaimsPrincipal.None);
        Assert.IsInstanceOfType<RetrieveAttachmentResult.NotFound>(result);
    }
}
