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
    private readonly ConcurrentDictionary<string, string?> _peerIdByAddress = new();

    public PeerAddressBook(IOptions<PeerSyncOptions> options)
    {
        foreach (var address in options.Value.SeedPeers)
            _peerIdByAddress.TryAdd(address, null);
    }

    public IReadOnlyCollection<string> KnownAddresses => _peerIdByAddress.Keys.ToList();

    public string? PeerIdFor(string address) => _peerIdByAddress.GetValueOrDefault(address);

    public void SetPeerId(string address, string peerId) => _peerIdByAddress[address] = peerId;

    public IReadOnlyList<KnownPeer> KnownPeers() =>
        _peerIdByAddress.Where(kv => kv.Value is not null).Select(kv => new KnownPeer(kv.Value!, kv.Key)).ToList();

    // Merges another site's own KnownPeers response -- new addresses are
    // added (PeerId unresolved until actually contacted); an address
    // already known keeps whatever PeerId this site has already resolved
    // for it, never overwritten by a peer's possibly-stale claim.
    public void Merge(IEnumerable<KnownPeer> knownPeers)
    {
        foreach (var peer in knownPeers)
            _peerIdByAddress.TryAdd(peer.Address, peer.PeerId);
    }
}
