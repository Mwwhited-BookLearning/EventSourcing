using System.Security.Cryptography.X509Certificates;
using EventStore.Spiffe;

namespace EventStore.Host.Core;

// Stands in for a real SPIRE Agent (EventStore.Spiffe.SpiffeSvidFactory's
// own comment) at the composition-root level: this Host generates its own
// throwaway trust-domain CA and self-issues its own peer-sync SVID once at
// startup, then builds a trust bundle from whichever other trust domains
// this deployment's configuration names as federated peers (ADR-048:
// "the other side's root is now in my bundle", nothing more). Constructed
// once per process -- every outbound peer-sync call and the internal mTLS
// listener share the exact same identity and bundle.
public class SpiffePeerIdentity
{
    public SpiffeId SelfId { get; }
    public X509Certificate2 SvidCertificate { get; }
    public SpiffeTrustBundle TrustBundle { get; }

    public SpiffePeerIdentity(SpiffePeerOptions options)
    {
        SelfId = SpiffeId.Parse($"spiffe://{options.TrustDomain}{options.ServicePath}");

        var ca = SpiffeSvidFactory.CreateTrustDomainCa(options.TrustDomain);
        SvidCertificate = SpiffeSvidFactory.IssueSvid(ca, SelfId);

        TrustBundle = new SpiffeTrustBundle();
        TrustBundle.AddTrustedRoot(options.TrustDomain, ca); // self-trust -- e.g. two replicas of this same site
        foreach (var peer in options.TrustedPeers)
            TrustBundle.AddTrustedRoot(peer.TrustDomain, X509CertificateLoader.LoadCertificate(Convert.FromBase64String(peer.RootCertificateBase64)));
    }
}
