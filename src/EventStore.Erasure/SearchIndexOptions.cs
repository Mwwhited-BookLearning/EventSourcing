namespace EventStore.Erasure;

// ADR-096 -- deliberately simpler than ErasureOptions' per-AppId backend
// map: a Shared-scope search-index key has no per-tenant data-residency
// driver the way a crypto-shredding DEK backend choice does (ADR-057's own
// reasoning for per-AppId selection), so one deployment-wide default is
// enough for now -- revisit if a real per-tenant need ever surfaces.
public class SearchIndexOptions
{
    public string DefaultBackend { get; set; } = "Local";
}
