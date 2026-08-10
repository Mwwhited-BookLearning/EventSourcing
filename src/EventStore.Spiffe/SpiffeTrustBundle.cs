using System.Security.Cryptography.X509Certificates;

namespace EventStore.Spiffe;

// One side's set of trusted root CA certificates, keyed by trust domain --
// what two independent SPIRE deployments exchange to federate (ADR-048):
// each side adds the other's root CA(s) to its own bundle. No shared
// central IdP or root CA is ever involved.
public class SpiffeTrustBundle
{
    private readonly Dictionary<string, List<X509Certificate2>> _rootsByTrustDomain = new();

    public void AddTrustedRoot(string trustDomain, X509Certificate2 rootCertificate)
    {
        if (!_rootsByTrustDomain.TryGetValue(trustDomain, out var roots))
            _rootsByTrustDomain[trustDomain] = roots = [];
        roots.Add(rootCertificate);
    }

    public IReadOnlyList<X509Certificate2> RootsFor(string trustDomain) =>
        _rootsByTrustDomain.TryGetValue(trustDomain, out var roots) ? roots : [];
}
