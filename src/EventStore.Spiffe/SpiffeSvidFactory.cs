using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Spiffe;

// Stands in for a real SPIRE Server + Agent (Go infrastructure, not a NuGet
// package -- docs/libraries/dotnet/spiffe-spire.md) for local dev/test use,
// the same role EventStore.DevIdp plays for OAuth2/OIDC: issues a
// self-signed trust-domain root CA, then short-lived leaf SVIDs signed by
// it, each carrying a spiffe://<trust-domain>/<path> SAN URI -- an ordinary
// X509Certificate2 a real SPIRE Agent could equally have written to disk
// (that library doc's "Option A"). Never presented as, or confused with,
// a production certificate authority.
public static class SpiffeSvidFactory
{
    public static X509Certificate2 CreateTrustDomainCa(string trustDomain, TimeSpan? validity = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={trustDomain} root CA", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore + (validity ?? TimeSpan.FromDays(1));
        var ca = request.CreateSelfSigned(notBefore, notAfter);

        // A cert minted via CreateSelfSigned carries an ephemeral private key not
        // reliably usable to sign a second certificate request via
        // X509SignatureGenerator on every platform -- round-tripping through a
        // PFX byte export/import, the same fix IssueSvid's own leaf needs below,
        // makes the private key handle a normal, reusable one.
        return X509CertificateLoader.LoadPkcs12(ca.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
    }

    public static X509Certificate2 IssueSvid(X509Certificate2 caCertificateWithPrivateKey, SpiffeId spiffeId, TimeSpan? validity = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={spiffeId}", key, HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri(spiffeId.ToString()));
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], critical: false)); // serverAuth + clientAuth -- mTLS needs both

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore + (validity ?? TimeSpan.FromHours(1)); // short-lived, per ADR-048 -- rotation, not long validity, is the mechanism

        using var caPrivateKey = caCertificateWithPrivateKey.GetECDsaPrivateKey()!;
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var leafWithoutKey = request.Create(
            caCertificateWithPrivateKey.SubjectName, X509SignatureGenerator.CreateForECDsa(caPrivateKey), notBefore, notAfter, serialNumber);
        var leafWithKey = leafWithoutKey.CopyWithPrivateKey(key);

        // Same PFX round-trip as CreateTrustDomainCa's own comment explains --
        // needed here too, since this leaf is what actually gets attached to an
        // HttpClientHandler/SslStream as a client certificate.
        return X509CertificateLoader.LoadPkcs12(leafWithKey.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
    }
}
