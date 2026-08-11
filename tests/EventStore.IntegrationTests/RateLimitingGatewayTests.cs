extern alias GatewayAssembly;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Per-Tenant Rate Limiting" (docs/08-build-plan.md, ADR-058) -- proven
// against the REAL EventStore.Gateway process (the same
// WebApplicationFactory<GatewayAssembly::Program> + real stand-in backend
// pattern GatewayTests.cs already established), not an isolated
// RateLimiterOptions unit test -- this item's whole risk is in the actual
// ASP.NET Core RateLimiting + YARP RouteConfig.RateLimiterPolicy wiring,
// not in isolated arithmetic. Every test uses its own tight, test-only
// limit override (RateLimiting:* settings), not this Gateway's own
// production defaults, so each scenario completes in milliseconds rather
// than needing to genuinely send dozens of requests.
[TestClass]
public class RateLimitingGatewayTests
{
    private static async Task<(WebApplication Backend, string Address, Func<int> HitCount)> StartBackendAsync(Func<HttpContext, Task>? onRequest = null)
    {
        var hitCount = 0;
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var backend = backendBuilder.Build();
        backend.MapPost("/{**catch-all}", async (HttpContext context) =>
        {
            Interlocked.Increment(ref hitCount);
            if (onRequest is not null)
                await onRequest(context);
            return Results.Ok(new { });
        });
        await backend.StartAsync();
        var address = backend.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (backend, address, () => hitCount);
    }

    private static WebApplicationFactory<GatewayAssembly::Program> CreateGatewayFactory(string backendAddress, params (string Key, string Value)[] settings)
    {
        return new WebApplicationFactory<GatewayAssembly::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ReverseProxy:Clusters:host-cluster:Destinations:d1:Address", backendAddress + "/");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });
    }

    private static HttpRequestMessage PublishRequest(string appId) => new(HttpMethod.Post, "/publish/OrderPlaced")
    {
        Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = "{}" }),
    };

    // TokenBucketRateLimiterOptions.TokensPerPeriod must be > 0 (a real
    // System.Threading.RateLimiting constraint, confirmed via a throwaway
    // probe -- passing 0 to mean "never replenish" throws
    // ArgumentException at first use, not at options-construction time).
    // A 1-hour ReplenishmentPeriod with TokensPerPeriod=1 achieves the same
    // "won't replenish during this test's own lifetime" effect legally.
    private const string NoReplenishmentWithinTestLifetime = "01:00:00";

    [TestMethod]
    public async Task ABurstWithinTheTokenBucketsCapacityIsNeverThrottled()
    {
        var (backend, address, _) = await StartBackendAsync();
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:PublishTokenLimit", "5"), ("RateLimiting:PublishTokensPerPeriod", "1"), ("RateLimiting:PublishReplenishmentPeriod", NoReplenishmentWithinTestLifetime));
        using var client = gatewayFactory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.SendAsync(PublishRequest("rate-demo-1"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"request {i} within the bucket's own capacity must not be throttled");
        }

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task SustainedPublishVolumePastTheTokenBucketLimitReceives429WithRetryAfter()
    {
        var (backend, address, hitCount) = await StartBackendAsync();
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:PublishTokenLimit", "3"), ("RateLimiting:PublishTokensPerPeriod", "1"), ("RateLimiting:PublishReplenishmentPeriod", NoReplenishmentWithinTestLifetime));
        using var client = gatewayFactory.CreateClient();

        for (var i = 0; i < 3; i++)
            using (var response = await client.SendAsync(PublishRequest("rate-demo-2")))
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var rejected = await client.SendAsync(PublishRequest("rate-demo-2"));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.IsTrue(rejected.Headers.Contains("Retry-After"), "a 429 must carry Retry-After");
        Assert.AreEqual(3, hitCount(), "the rejected request must never reach the backend");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task OneTenantExhaustingItsTokenBucketNeverAffectsADifferentTenant()
    {
        var (backend, address, _) = await StartBackendAsync();
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:PublishTokenLimit", "1"), ("RateLimiting:PublishTokensPerPeriod", "1"), ("RateLimiting:PublishReplenishmentPeriod", NoReplenishmentWithinTestLifetime));
        using var client = gatewayFactory.CreateClient();

        using (var first = await client.SendAsync(PublishRequest("rate-demo-tenant-a")))
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using (var exhausted = await client.SendAsync(PublishRequest("rate-demo-tenant-a")))
            Assert.AreEqual(HttpStatusCode.TooManyRequests, exhausted.StatusCode, "tenant-a's own bucket is now empty");

        // A completely different AppId holds its OWN, independent bucket --
        // tenant-a exhausting its own share has zero effect here.
        using (var otherTenant = await client.SendAsync(PublishRequest("rate-demo-tenant-b")))
            Assert.AreEqual(HttpStatusCode.OK, otherTenant.StatusCode, "a different tenant's own bucket is unaffected by tenant-a's exhaustion");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task AConcurrencyLimitedFollowConnectionIsRejectedBeyondItsPermitLimitWhileExistingConnectionsStayOpen()
    {
        // The backend holds each request open until this test explicitly
        // releases it -- otherwise a fast in-memory round trip would
        // complete before a second request could ever observe the first
        // one still "holding" a concurrency slot.
        var releaseFirstRequest = new TaskCompletionSource();
        var firstRequestArrived = new TaskCompletionSource();
        var (backend, address, _) = await StartBackendAsync(async context =>
        {
            if (context.Request.Path == "/follow/RoleGranted")
            {
                firstRequestArrived.TrySetResult();
                await releaseFirstRequest.Task;
            }
        });
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:FollowConcurrencyLimit", "1"));
        using var client = gatewayFactory.CreateClient();

        var firstRequestTask = client.PostAsync("/follow/RoleGranted", JsonContent.Create(new { appId = "rate-demo-3" }));
        await firstRequestArrived.Task; // the first connection now genuinely holds the ONLY permitted concurrency slot

        // ConcurrencyLimiter never attaches Retry-After metadata (confirmed
        // via a throwaway probe of System.Threading.RateLimiting directly)
        // -- unlike a Token Bucket or Fixed Window, there is no fixed
        // replenishment schedule to report a wait time for; a slot frees
        // the instant ANY in-flight request completes, which could be
        // sooner or later than any fixed hint this limiter might invent.
        // RateLimiterPolicies.OnRejected already guards with TryGetMetadata,
        // so this just omits the header rather than sending a fabricated one.
        using var secondResponse = await client.PostAsync("/follow/RoleGranted", JsonContent.Create(new { appId = "rate-demo-3" }));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, secondResponse.StatusCode, "a second concurrent connection beyond the permit limit must be rejected");

        releaseFirstRequest.SetResult();
        using var firstResponse = await firstRequestTask;
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode, "the first connection was never affected by the second one's own rejection");

        // Closing the first connection frees its slot -- a new one now succeeds.
        using var thirdResponse = await client.PostAsync("/follow/RoleGranted", JsonContent.Create(new { appId = "rate-demo-3" }));
        Assert.AreEqual(HttpStatusCode.OK, thirdResponse.StatusCode, "closing a connection must free its slot for a new one");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task ASlidingWindowQueryLimitRejectsWith429OnceExceeded()
    {
        var (backend, address, hitCount) = await StartBackendAsync();
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:GeneralPermitLimit", "2"), ("RateLimiting:GeneralWindow", "00:01:00"), ("RateLimiting:GeneralSegmentsPerWindow", "1"));
        using var client = gatewayFactory.CreateClient();

        for (var i = 0; i < 2; i++)
            using (var response = await client.PostAsync("/graphql", JsonContent.Create(new { query = "{ __typename }" })))
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // SlidingWindowRateLimiter never attaches Retry-After metadata
        // either (confirmed alongside ConcurrencyLimiter in the same probe)
        // -- only TokenBucketRateLimiter does in this API version. The 429
        // itself, and the backend never seeing the rejected request, are
        // what this policy actually guarantees.
        using var rejected = await client.PostAsync("/graphql", JsonContent.Create(new { query = "{ __typename }" }));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.AreEqual(2, hitCount(), "the rejected query must never reach the backend");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task PassingTheGatewaysRateLimiterDoesNotBypassTheHostsOwnGraphQlDepthLimiter()
    {
        // ADR-058 (this Gateway's rate limiter) and ADR-037 (the Host's
        // GraphQL execution-depth limiter, already proven directly against
        // a real Host in GraphQlHttpSqliteTests.
        // ADeeplyNestedIntrospectionQueryIsRejectedByTheDepthLimiter)
        // are two fully independent mechanisms in two separate processes --
        // there is no shared state or middleware ordering between them that
        // could let one substitute for or exempt the other. What THIS
        // Gateway could get wrong is forwarding a GraphQL request in a way
        // that alters it (rewriting the body, stripping/mutating it while
        // resolving a rate-limit partition key) before the Host ever sees
        // it, which would silently change what the Host's depth limiter is
        // actually evaluating. AppIdBufferingMiddleware only reads the
        // Gateway's OWN /publish and /follow bodies (see its ShouldPeekBody
        // guard) -- /graphql traffic is untouched -- so this test proves a
        // query that clears the Gateway's general sliding-window policy
        // still arrives at the backend byte-for-byte, the same "pass predicate,
        // reach a well-formed backend request" this file's other Gateway-side
        // limiter tests use to reason about the Gateway's own passthrough.
        var deepQuery = "query { __schema { types { fields { type { fields { type { name } } } } } } }";
        string? receivedBody = null;
        var (backend, address, hitCount) = await StartBackendAsync(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            receivedBody = await reader.ReadToEndAsync();
        });
        using var gatewayFactory = CreateGatewayFactory(address, ("RateLimiting:GeneralPermitLimit", "5"));
        using var client = gatewayFactory.CreateClient();

        using var response = await client.PostAsync("/graphql", JsonContent.Create(new { query = deepQuery }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "a query within the Gateway's own general rate limit is forwarded, not rejected here");
        Assert.AreEqual(1, hitCount());
        Assert.IsNotNull(receivedBody);
        Assert.Contains(deepQuery, receivedBody!, "the Gateway must forward the GraphQL request byte-for-byte -- the Host's own depth limiter, not this Gateway, is what evaluates query shape");

        await backend.StopAsync();
    }

    [TestMethod]
    public async Task ATenantsLimitIsChangeableViaConfigurationAloneNoCodeDeploy()
    {
        var (backend, address, _) = await StartBackendAsync();

        // Same Gateway code, two different WebApplicationFactory instances
        // built with different RateLimiting:PublishTokenLimit settings --
        // exactly what "configuration, not code" means: the exact same
        // binary behaves differently purely because of what's in
        // appsettings/environment, with no rebuild between them.
        using (var tightFactory = CreateGatewayFactory(address, ("RateLimiting:PublishTokenLimit", "1"), ("RateLimiting:PublishTokensPerPeriod", "1"), ("RateLimiting:PublishReplenishmentPeriod", NoReplenishmentWithinTestLifetime)))
        using (var tightClient = tightFactory.CreateClient())
        {
            using (var first = await tightClient.SendAsync(PublishRequest("rate-demo-config-1")))
                Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
            using var rejected = await tightClient.SendAsync(PublishRequest("rate-demo-config-1"));
            Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode, "a 1-token bucket rejects its 2nd request");
        }

        using (var looseFactory = CreateGatewayFactory(address, ("RateLimiting:PublishTokenLimit", "5"), ("RateLimiting:PublishTokensPerPeriod", "1"), ("RateLimiting:PublishReplenishmentPeriod", NoReplenishmentWithinTestLifetime)))
        using (var looseClient = looseFactory.CreateClient())
        {
            for (var i = 0; i < 5; i++)
                using (var response = await looseClient.SendAsync(PublishRequest("rate-demo-config-2")))
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"a 5-token bucket permits request {i}");
        }

        await backend.StopAsync();
    }
}
