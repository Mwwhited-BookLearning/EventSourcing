namespace EventStore.Replication;

// ADR-051 -- discovery by explicit static configuration, no automatic
// discovery of any kind. SeedPeers only ever needs to name a subset of
// currently-live sites, not every site -- ADR-033's gossip protocol
// (PeerSyncWorker's own knownPeers exchange) discovers the rest of the
// mesh from whichever seed answers first.
public class PeerSyncOptions
{
    public List<string> SeedPeers { get; set; } = [];

    // How many of this site's own not-yet-acked events to push to a peer
    // in one batch -- bounds a single sync tick's own request size.
    public int BatchSize { get; set; } = 500;
}
