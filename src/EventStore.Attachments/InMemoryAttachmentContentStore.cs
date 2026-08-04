using System.Collections.Concurrent;

namespace EventStore.Attachments;

// The one registered IAttachmentContentStore backend at this build stage --
// a dev/POC-scale, process-local store, the same accepted-cost posture
// EventStore.Dpop's InMemoryDpopReplayCache already uses for a similarly-
// scoped mechanism. Not actually on AttachmentService's own default
// creation path yet (ADR-032's own "ContentProviderKey: null means 'this
// table'" -- the v1 engine choice is Attachment.Bytes directly, no
// indirection); this exists so the keyed-DI seam itself is real,
// registrable, and round-trip-testable, ready for a real deployment to
// register an actual Azure Blob/S3-backed implementation alongside or
// instead of this one, and for a future tiering mover to use once built.
public class InMemoryAttachmentContentStore : IAttachmentContentStore
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public Task<string> StoreAsync(byte[] bytes, CancellationToken ct = default)
    {
        var providerRef = Guid.NewGuid().ToString("N");
        _blobs[providerRef] = bytes;
        return Task.FromResult(providerRef);
    }

    public Task<byte[]> RetrieveAsync(string contentProviderRef, CancellationToken ct = default) =>
        Task.FromResult(_blobs.TryGetValue(contentProviderRef, out var bytes)
            ? bytes
            : throw new KeyNotFoundException($"No content stored under provider ref '{contentProviderRef}'"));
}
