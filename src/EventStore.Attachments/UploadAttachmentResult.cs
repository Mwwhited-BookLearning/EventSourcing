namespace EventStore.Attachments;

public abstract record UploadAttachmentResult
{
    // A genuinely new upload -- a new Attachment row was created.
    public sealed record Created(string ContentHash) : UploadAttachmentResult;

    // ADR-011/032's dedup-by-content-equality reasoning, applied to blobs:
    // identical bytes were already stored under this hash -- the existing
    // object is reused, no second copy created.
    public sealed record Deduplicated(string ContentHash) : UploadAttachmentResult;

    // ADR-032 -- a RequiredPublishClaim gates re-upload of already-stored
    // bytes (a ContentHash collision), never the first upload that
    // actually creates the Attachment row.
    public sealed record Forbidden : UploadAttachmentResult;

    private UploadAttachmentResult() { }
}

public abstract record RetrieveAttachmentResult
{
    public sealed record Found(byte[] Bytes, string MimeType, string? FileName) : RetrieveAttachmentResult;

    public sealed record NotFound : RetrieveAttachmentResult;

    // ADR-032's direct-claim precedence -- a direct RequiredReadClaim on the
    // Attachment itself always governs if set. Inheriting a linked
    // event's/entity's own claim absent a direct one is explicitly not
    // designed further in that ADR and not built at this stage.
    public sealed record Forbidden : RetrieveAttachmentResult;

    private RetrieveAttachmentResult() { }
}
