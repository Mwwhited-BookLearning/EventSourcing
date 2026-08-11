using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace EventStore.Spiffe;

public static class SpiffeKestrelExtensions
{
    // A dedicated internal HTTPS listener, on its own port, requiring a
    // client certificate SpiffeCertificateValidator accepts -- ADR-048's
    // own exit criterion, "a request bearing no valid SVID is rejected at
    // the mTLS handshake, before it reaches application code." Deliberately
    // a SEPARATE listener from the external one (ADR-006's ordinary
    // callers never carry a client certificate at all) rather than a
    // KestrelServerOptions.ConfigureHttpsDefaults change, which would apply
    // to every listener including the external one.
    public static void ListenInternalMtls(
        this KestrelServerOptions kestrelOptions, IPEndPoint endpoint,
        X509Certificate2 serverCertificate, SpiffeTrustBundle trustBundle, Func<SpiffeId, bool> isAllowedPeer)
    {
        kestrelOptions.Listen(endpoint, listenOptions => listenOptions.UseHttps(ConfigureHttps(serverCertificate, trustBundle, isAllowedPeer)));
    }

    private static Action<HttpsConnectionAdapterOptions> ConfigureHttps(
        X509Certificate2 serverCertificate, SpiffeTrustBundle trustBundle, Func<SpiffeId, bool> isAllowedPeer) => httpsOptions =>
    {
        httpsOptions.ServerCertificate = serverCertificate;
        httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        httpsOptions.ClientCertificateValidation = (certificate, _, _) =>
            SpiffeCertificateValidator.Validate(certificate, trustBundle, isAllowedPeer) is SpiffeValidationResult.Accepted;
    };
}
