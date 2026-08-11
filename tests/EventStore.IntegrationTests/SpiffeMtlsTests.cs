using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using EventStore.Spiffe;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-048's own exit criteria, proven against a REAL Kestrel HTTPS listener
// and a REAL TLS handshake -- not mocked -- since "rejected at the mTLS
// handshake, before it reaches application code" is a claim about the
// transport layer itself. Unlike every provider-specific item's tests,
// this doesn't touch EventStoreContext/a database provider at all, so it's
// exercised once here, not x3 -- the behavior is identical regardless of
// which provider a Host runs against.
[TestClass]
public class SpiffeMtlsTests
{
    [TestMethod]
    public async Task AllSpiffeMtlsScenarios()
    {
        var caA = SpiffeSvidFactory.CreateTrustDomainCa("eventstore.site-a");
        var caB = SpiffeSvidFactory.CreateTrustDomainCa("eventstore.site-b");
        var untrustedCa = SpiffeSvidFactory.CreateTrustDomainCa("eventstore.untrusted");

        var serverId = SpiffeId.Parse("spiffe://eventstore.site-a/eventstore/peer-sync");
        var serverCert = SpiffeSvidFactory.IssueSvid(caA, serverId);

        // Site A's own bundle: trusts its own domain, plus site B's --
        // simulating "already federated" (the trust-bundle-exchange step
        // itself is unit-tested directly against SpiffeCertificateValidator,
        // without needing a real socket, elsewhere).
        var trustBundle = new SpiffeTrustBundle();
        trustBundle.AddTrustedRoot("eventstore.site-a", caA);
        trustBundle.AddTrustedRoot("eventstore.site-b", caB);

        await using var app = BuildApp(serverCert, trustBundle, id => id.Path == "/eventstore/peer-sync");
        await app.StartAsync();
        var port = GetBoundPort(app);

        var validPeerCert = SpiffeSvidFactory.IssueSvid(caB, SpiffeId.Parse("spiffe://eventstore.site-b/eventstore/peer-sync"));
        Assert.IsTrue(await CanConnectAsync(port, validPeerCert), "a federated peer's own SVID must be accepted");

        var untrustedCert = SpiffeSvidFactory.IssueSvid(untrustedCa, SpiffeId.Parse("spiffe://eventstore.untrusted/eventstore/peer-sync"));
        Assert.IsFalse(await CanConnectAsync(port, untrustedCert), "a cert from an untrusted CA must be rejected at the handshake");

        var wrongIdentityCert = SpiffeSvidFactory.IssueSvid(caB, SpiffeId.Parse("spiffe://eventstore.site-b/eventstore/gateway"));
        Assert.IsFalse(await CanConnectAsync(port, wrongIdentityCert), "a trusted-CA cert for a disallowed SPIFFE ID must still be rejected");

        Assert.IsFalse(await CanConnectAsync(port, clientCertificate: null), "no client certificate at all must be rejected (RequireCertificate)");

        await app.StopAsync();
    }

    // ADR-049 -- a Host fronted by EventStore.Gateway adds the gateway's own
    // SPIFFE ID path to AllowedInternalCallerPaths, so the SAME internal
    // mTLS listener (not a second one) accepts both peer-sync connections
    // and gateway-forwarded traffic, each still checked against the same
    // trust bundle.
    [TestMethod]
    public async Task GatewayAndPeerIdentitiesShareOneInternalListenerWhenBothAreAllowed()
    {
        var siteCa = SpiffeSvidFactory.CreateTrustDomainCa("eventstore.shared-listener");
        var serverCert = SpiffeSvidFactory.IssueSvid(siteCa, SpiffeId.Parse("spiffe://eventstore.shared-listener/eventstore/peer-sync"));

        var trustBundle = new SpiffeTrustBundle();
        trustBundle.AddTrustedRoot("eventstore.shared-listener", siteCa);

        var allowedPaths = new HashSet<string> { "/eventstore/peer-sync", "/eventstore/gateway" };
        await using var app = BuildApp(serverCert, trustBundle, id => allowedPaths.Contains(id.Path));
        await app.StartAsync();
        var port = GetBoundPort(app);

        var peerCert = SpiffeSvidFactory.IssueSvid(siteCa, SpiffeId.Parse("spiffe://eventstore.shared-listener/eventstore/peer-sync"));
        Assert.IsTrue(await CanConnectAsync(port, peerCert), "a peer identity must still be accepted");

        var gatewayCert = SpiffeSvidFactory.IssueSvid(siteCa, SpiffeId.Parse("spiffe://eventstore.shared-listener/eventstore/gateway"));
        Assert.IsTrue(await CanConnectAsync(port, gatewayCert), "the gateway's own identity must now also be accepted");

        var otherCert = SpiffeSvidFactory.IssueSvid(siteCa, SpiffeId.Parse("spiffe://eventstore.shared-listener/eventstore/router"));
        Assert.IsFalse(await CanConnectAsync(port, otherCert), "an identity not named in AllowedInternalCallerPaths is still rejected");

        await app.StopAsync();
    }

    private static WebApplication BuildApp(X509Certificate2 serverCert, SpiffeTrustBundle trustBundle, Func<SpiffeId, bool> isAllowed)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenInternalMtls(new IPEndPoint(IPAddress.Loopback, 0), serverCert, trustBundle, isAllowed));
        var app = builder.Build();
        app.MapGet("/", () => "ok");
        return app;
    }

    private static int GetBoundPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        return new Uri(addresses.Single()).Port;
    }

    private static async Task<bool> CanConnectAsync(int port, X509Certificate2? clientCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                // This test proves the SERVER's own client-certificate gate, not
                // certificate pinning on the client side -- the dev SVID's issuing
                // CA is deliberately not in any trusted root store.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
        if (clientCertificate is not null)
            handler.SslOptions.ClientCertificates = [clientCertificate];

        using var client = new HttpClient(handler);
        try
        {
            using var response = await client.GetAsync($"https://localhost:{port}/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
