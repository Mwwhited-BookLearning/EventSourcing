namespace EventStore.Attachments;

// ADR-032's Consequences -- "the exact same Strategy-pattern/keyed-DI
// shape ADR-057's IErasureKeyStore already established, applied here to
// content storage instead of encryption keys." A registered backend
// stores opaque bytes under an opaque provider-specific reference; it
// has no opinion on ContentHash, chunking, or anything else Attachment-
// specific -- purely "bytes in, a locator out" and "a locator in, bytes
// out." Multiple backends (Azure Blob tiers, S3 storage classes, this
// build stage's own DB-table default) can be registered simultaneously,
// each under its own key, for a future tiering mover ("Event Log/AccessLog
// Archival Segment Detachment" names this interface as a dependency,
// reused unchanged) to move bytes between.
public interface IAttachmentContentStore
{
    Task<string> StoreAsync(byte[] bytes, CancellationToken ct = default);

    Task<byte[]> RetrieveAsync(string contentProviderRef, CancellationToken ct = default);
}
