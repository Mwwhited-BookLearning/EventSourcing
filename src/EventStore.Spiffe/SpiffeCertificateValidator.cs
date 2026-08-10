using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Spiffe;

public static class SpiffeCertificateValidator
{
    // Validates a presented leaf certificate two ways, per ADR-048: (1) its
    // SAN URI is a well-formed SPIFFE ID that isAllowed accepts (e.g. "one
    // of my configured peers", "my own gateway"); (2) it chains to a root
    // this bundle actually trusts for that SPIFFE ID's own trust domain --
    // federation is exactly "the other side's root is now in my bundle",
    // nothing more. Either failing is one rejection, no partial credit --
    // this is the gate behind ADR-048's own exit criterion, "a request
    // bearing no valid SVID is rejected... before it reaches application
    // code."
    public static SpiffeValidationResult Validate(
        X509Certificate2 leafCertificate, SpiffeTrustBundle trustBundle, Func<SpiffeId, bool> isAllowed)
    {
        var spiffeId = ExtractSpiffeId(leafCertificate);
        if (spiffeId is null)
            return new SpiffeValidationResult.Rejected("certificate carries no SPIFFE ID SAN URI");

        if (!isAllowed(spiffeId))
            return new SpiffeValidationResult.Rejected($"{spiffeId} is not an allowed peer identity");

        var roots = trustBundle.RootsFor(spiffeId.TrustDomain);
        if (roots.Count == 0)
            return new SpiffeValidationResult.Rejected($"trust domain {spiffeId.TrustDomain} is not in this bundle");

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // short-lived SVIDs (ADR-048) -- rotation, not revocation, is the mechanism
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        foreach (var root in roots)
            chain.ChainPolicy.CustomTrustStore.Add(root);

        return chain.Build(leafCertificate)
            ? new SpiffeValidationResult.Accepted(spiffeId)
            : new SpiffeValidationResult.Rejected($"certificate does not chain to a trusted root for {spiffeId.TrustDomain}");
    }

    // X509SubjectAlternativeNameExtension only exposes EnumerateDnsNames/
    // EnumerateIPAddresses -- no EnumerateUris -- so a URI-typed GeneralName
    // (RFC 5280 SS4.2.1.6's [6] uniformResourceIdentifier, an IA5String under
    // an implicit context-specific tag) needs a direct ASN.1 read off the
    // extension's own RawData instead.
    private static SpiffeId? ExtractSpiffeId(X509Certificate2 certificate)
    {
        var sanExtension = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (sanExtension is null)
            return null;

        var reader = new AsnReader(sanExtension.RawData, AsnEncodingRules.DER);
        var generalNames = reader.ReadSequence();
        while (generalNames.HasData)
        {
            var tag = generalNames.PeekTag();
            if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 6) // uniformResourceIdentifier
            {
                var uri = generalNames.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                if (SpiffeId.TryParse(uri, out var spiffeId))
                    return spiffeId;
            }
            else
            {
                generalNames.ReadEncodedValue();
            }
        }

        return null;
    }
}
