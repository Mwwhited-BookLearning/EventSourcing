using System.Security.Claims;
using System.Security.Cryptography;
using EventStore.Domain.SchemaRegistry;
using EventStore.Domain.Streaming;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Attachments;

// ADR-032 -- content-addressed by construction: ContentHash (SHA-256 of
// the raw bytes, the same primitive ADR-011/019 already use) is the real
// primary key in spirit. Storage stays out-of-band from the event log,
// the same posture ADR-031 already takes for streaming channels. This
// build stage's v1 engine choice: Attachment.Bytes directly, no
// IAttachmentContentStore indirection -- "ContentProviderKey: null means
// this table," per that field's own doc comment.
public class AttachmentService(EventStoreContext db)
{
    public async Task<UploadAttachmentResult> UploadAsync(
        byte[] bytes, string mimeType, string? fileName, string? requiredReadClaim, string? requiredPublishClaim,
        ClaimsPrincipal user, CancellationToken ct = default)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var existing = await db.Attachments.SingleOrDefaultAsync(a => a.ContentHash == contentHash, ct);
        if (existing is not null)
        {
            // ADR-032 -- gates re-upload of already-stored bytes only; the
            // first upload below (which actually creates the row) has
            // nothing to check against yet.
            if (existing.RequiredPublishClaim is { } publishClaim && !RequiredClaimEvaluator.HasClaim(user, publishClaim))
                return new UploadAttachmentResult.Forbidden();

            existing.LastAccessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return new UploadAttachmentResult.Deduplicated(contentHash);
        }

        db.Attachments.Add(new Attachment
        {
            ContentHash = contentHash,
            Bytes = bytes,
            MimeType = mimeType,
            SizeBytes = bytes.LongLength,
            FileName = fileName,
            UploadedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
            RequiredReadClaim = requiredReadClaim,
            RequiredPublishClaim = requiredPublishClaim,
        });
        await db.SaveChangesAsync(ct);
        return new UploadAttachmentResult.Created(contentHash);
    }

    public async Task<RetrieveAttachmentResult> RetrieveAsync(string contentHash, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var attachment = await db.Attachments.SingleOrDefaultAsync(a => a.ContentHash == contentHash, ct);
        if (attachment is null)
            return new RetrieveAttachmentResult.NotFound();

        // ADR-032 -- a direct claim on the attachment always governs if
        // set. Inheriting a linked event's/entity's own claim absent a
        // direct one is explicitly not designed further in that ADR.
        if (attachment.RequiredReadClaim is { } claim && !RequiredClaimEvaluator.HasClaim(user, claim))
            return new RetrieveAttachmentResult.Forbidden();

        attachment.LastAccessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new RetrieveAttachmentResult.Found(attachment.Bytes!, attachment.MimeType, attachment.FileName);
    }

    // ADR-032 -- links an already-uploaded attachment to an entity and/or
    // a specific event, independently; either, both, or neither may be
    // set. Deduplication by ContentHash means an attachment is many-to-
    // many with events/entities by construction -- the same bytes
    // referenced twice is just two AttachmentRef rows, not two copies.
    public async Task LinkAsync(string contentHash, string? entityId, Guid? eventId, CancellationToken ct = default)
    {
        db.AttachmentRefs.Add(new AttachmentRef { ContentHash = contentHash, EntityId = entityId, EventId = eventId });
        await db.SaveChangesAsync(ct);
    }
}
