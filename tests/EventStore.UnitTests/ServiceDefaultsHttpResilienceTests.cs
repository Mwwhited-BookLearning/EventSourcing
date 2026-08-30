using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.UnitTests;

// docs/bugs/framework/service/follow-client-faults-under-default-http-
// resilience-timeout.md -- AddServiceDefaults used to wrap every HttpClient
// (via ConfigureHttpClientDefaults) in AddStandardResilienceHandler
// (Microsoft.Extensions.Http.Resilience/Polly v8)'s default 10s-per-attempt
// timeout. Its AttemptTimeout strategy wraps only the SendAsync call itself
// -- it has no way to see how long a caller spends reading the response body
// afterward -- so this proves the ACTUAL vulnerable phase directly: a
// backend that's simply slow to respond at all (contended at startup, a cold
// JIT/connection pool, real crypto work under concurrent load -- all
// genuinely observed in this project's own multi-service Aspire topology,
// not a hypothetical), not FollowClient's own long-lived SSE body-read
// (which happens entirely after SendAsync has already returned under
// HttpCompletionOption.ResponseHeadersRead, so it was never actually
// vulnerable to AttemptTimeout the way an earlier draft of this diagnosis
// assumed -- corrected before shipping, not left wrong).
[TestClass]
public class ServiceDefaultsHttpResilienceTests
{
    [TestMethod]
    public async Task AddServiceDefaultsAppliesNoAttemptTimeoutSoASlowButEventuallySuccessfulResponseIsNotAborted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient("SlowBackend")
            .ConfigurePrimaryHttpMessageHandler(() => new DelayedResponseHandler(TimeSpan.FromSeconds(12)));

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("SlowBackend");

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("http://localhost/slow");
        stopwatch.Stop();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "a slow-but-eventually-successful response must not be aborted by a default attempt timeout");
        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromSeconds(11),
            $"expected the real ~12s delay to have actually elapsed (no timeout cut it short), took {stopwatch.Elapsed}");
    }

    // A terminal (primary) handler, not a pass-through one -- deliberately
    // never calls base.SendAsync, so no InnerHandler is needed.
    private sealed class DelayedResponseHandler(TimeSpan delay) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
