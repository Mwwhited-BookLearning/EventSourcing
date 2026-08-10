namespace EventStore.Erasure;

// ADR-057 -- backend selection is per-AppId, ordinary configuration, not a
// single deployment-wide pick: a healthcare tenant's keys can sit in a
// self-hosted Vault while a different tenant in the same deployment uses
// the local store (or, once built, a cloud KMS).
public class ErasureOptions
{
    public string DefaultBackend { get; set; } = "Local";
    public Dictionary<string, string> BackendByAppId { get; set; } = new();

    public string BackendFor(string appId) => BackendByAppId.GetValueOrDefault(appId, DefaultBackend);
}
