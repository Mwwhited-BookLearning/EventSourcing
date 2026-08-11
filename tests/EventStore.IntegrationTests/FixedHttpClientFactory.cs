namespace EventStore.IntegrationTests;

// A minimal IHttpClientFactory for tests -- FollowClient asks for named
// clients ("Follow", "DevIdp"); this hands back the exact
// WebApplicationFactory-created (in-memory TestServer) HttpClients the test
// class already built, instead of pulling in the real Microsoft.Extensions.
// Http factory machinery just to route two fixed, already-configured clients.
internal sealed class FixedHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => clients[name];
}
