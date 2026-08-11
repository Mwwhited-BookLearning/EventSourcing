extern alias GatewayAssembly;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-049's own core claim -- one external entry point, routing to the
// right internal destination, the caller's own Authorization header
// forwarded unchanged rather than re-authenticated at the gateway --
// proven against the REAL EventStore.Gateway process (not a stand-in) and
// a real backend listener. Deliberately plain HTTP on the backend side:
// this test is about routing/header-forwarding correctness, not mTLS --
// ADR-048's own gateway-to-host SPIFFE identity claim is proven directly
// against ListenInternalMtls in SpiffeMtlsTests instead, where the
// federation/rejection mechanics are actually exercised.
[TestClass]
public class GatewayTests
{
    [TestMethod]
    public async Task ARequestThroughTheGatewayReachesTheBackendWithTheOriginalAuthorizationHeaderIntact()
    {
        string? receivedAuthorizationHeader = null;
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var backend = backendBuilder.Build();
        backend.MapGet("/{**catch-all}", (HttpRequest request) =>
        {
            receivedAuthorizationHeader = request.Headers.Authorization.ToString();
            return Results.Text("backend response");
        });
        await backend.StartAsync();
        var backendAddress = backend.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var gatewayFactory = new WebApplicationFactory<GatewayAssembly::Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ReverseProxy:Clusters:host-cluster:Destinations:d1:Address", backendAddress + "/"));
        using var gatewayClient = gatewayFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/publish/OrderPlaced");
        request.Headers.Add("Authorization", "Bearer test-token-should-pass-through-unchanged");

        using var response = await gatewayClient.SendAsync(request);

        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.AreEqual("backend response", await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Bearer test-token-should-pass-through-unchanged", receivedAuthorizationHeader);

        await backend.StopAsync();
    }
}
