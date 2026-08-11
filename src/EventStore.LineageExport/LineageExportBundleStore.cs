using Microsoft.Extensions.Caching.Memory;

namespace EventStore.LineageExport;

// ADR-068 -- "a produced artifact, never stored server-side beyond its
// retrieval window." An in-memory, short-TTL cache, not a persisted table
// (there is genuinely nothing to back up or migrate) -- the export is
// meant to leave this system as a downloaded file, not live in it.
public class LineageExportBundleStore(IMemoryCache cache)
{
    private static readonly TimeSpan RetrievalWindow = TimeSpan.FromMinutes(15);

    public string Store(LineageExportBundle bundle)
    {
        var exportId = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(exportId), bundle, RetrievalWindow);
        return exportId;
    }

    public LineageExportBundle? TryGet(string exportId) =>
        cache.TryGetValue(CacheKey(exportId), out LineageExportBundle? bundle) ? bundle : null;

    private static string CacheKey(string exportId) => $"lineage-export:{exportId}";
}
