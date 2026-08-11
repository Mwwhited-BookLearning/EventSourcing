namespace EventStore.Replication;

public class PeerSyncClientOptions
{
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string Scope { get; set; } = "peer:sync";
}
