namespace EventStore.Host.Core;

public class SpiffePeerOptions
{
    // A dev default -- a real deployment overrides this per site. Never
    // shared across genuinely independent sites; two sites under the same
    // trust domain would trust each other's SVIDs unconditionally.
    public string TrustDomain { get; set; } = "eventstore.local";
    public string ServicePath { get; set; } = "/eventstore/peer-sync";

    // null = the internal mTLS listener is not started -- e.g. under test,
    // or a single-site deployment with no peers at all. Set to a real port
    // to actually accept peer-sync connections (ADR-048).
    public int? InternalListenPort { get; set; }

    // Which SPIFFE ID paths may connect to the internal mTLS listener --
    // empty (the default) means ServicePath alone, i.e. peers only. A Host
    // fronted by EventStore.Gateway (ADR-049) adds the gateway's own
    // ServicePath ("/eventstore/gateway") here too, so the SAME internal
    // listener -- not a second one -- accepts both peer-sync connections
    // and gateway-forwarded traffic.
    public List<string> AllowedInternalCallerPaths { get; set; } = [];

    public List<TrustedPeerDomain> TrustedPeers { get; set; } = [];
}

// One entry per federated peer trust domain (ADR-048): the peer's own
// exported root CA certificate, added to this site's trust bundle so that
// site's SVIDs are accepted -- and nothing more. Discovered/configured
// statically, the same "explicit configuration, no automatic discovery"
// posture ADR-051's SeedPeers already established for peer addresses.
public class TrustedPeerDomain
{
    public string TrustDomain { get; set; } = default!;
    public string RootCertificateBase64 { get; set; } = default!; // DER-encoded X.509, base64
}
