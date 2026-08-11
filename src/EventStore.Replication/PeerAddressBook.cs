using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace EventStore.Replication;

// A runtime-only registry of "PeerId -> Address," seeded from
// PeerSyncOptions.SeedPeers (address-only -- PeerId isn't known until this
// site actually contacts that address and learns it via /peer-sync/whoami)
// and grown by every push/ack round trip's own KnownPeers exchange
// (ADR-051). Deliberately NOT durable -- losing it on restart is
// acceptable, since SeedPeers config is always available again at
// startup and rediscovery through it is exactly how a fresh peer is
// expected to (re)join the mesh; the durable state that must survive a
// restart is PeerSyncCursor, not this bootstrap aid.
public class PeerAddressBook
{
    // Keyed by Address until a PeerId is learned for it (Address as its
    // own temporary key), then re-keyed by the real PeerId once known.
    // Region (ADR-061) travels alongside PeerId -- learned the same two
    // ways: directly via this site's own /peer-sync/whoami call to that
    // address, or transitively via another peer's own KnownPeers gossip.
    private readonly ConcurrentDictionary<string, (string? PeerId, string? Region)> _peerByAddress = new();

    public PeerAddressBook(IOptions<PeerSyncOptions> options)
    {
        foreach (var address in options.Value.SeedPeers)
            _peerByAddress.TryAdd(address, (null, null));
    }

    public IReadOnlyCollection<string> KnownAddresses => _peerByAddress.Keys.ToList();

    public string? PeerIdFor(string address) => _peerByAddress.GetValueOrDefault(address).PeerId;

    public string? RegionFor(string address) => _peerByAddress.GetValueOrDefault(address).Region;

    public void SetPeerIdAndRegion(string address, string peerId, string? region) => _peerByAddress[address] = (peerId, region);

    public IReadOnlyList<KnownPeer> KnownPeers() =>
        _peerByAddress.Where(kv => kv.Value.PeerId is not null)
            .Select(kv => new KnownPeer(kv.Value.PeerId!, kv.Key, kv.Value.Region)).ToList();

    // Merges another site's own KnownPeers response -- new addresses are
    // added (PeerId/Region unresolved until actually contacted); an
    // address already known keeps whatever this site has already resolved
    // for it, never overwritten by a peer's possibly-stale claim.
    public void Merge(IEnumerable<KnownPeer> knownPeers)
    {
        foreach (var peer in knownPeers)
            _peerByAddress.TryAdd(peer.Address, (peer.PeerId, peer.Region));
    }
}
