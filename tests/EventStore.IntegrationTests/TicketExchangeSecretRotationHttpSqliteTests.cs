extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Dpop;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;

namespace EventStore.IntegrationTests;

// ADR-093 -- real zero-downtime rotation for ADR-040's ticket-exchange
// shared secret (the client_id path's own client_secret). Entirely a
// DevIdp-internal concern -- both /connect/token's token-exchange branch
// and /oauth/introspect live in EventStore.DevIdp itself, so unlike
// TicketExchangeHttpSqliteTests (which needs a real Host to prove a ticket
// actually unlocks header-incapable retrieval), this only needs the DevIdp
// TestServer alone to prove the rotation mechanics themselves.
// [assembly: Parallelize(Scope = ExecutionScope.MethodLevel)] (MSTestSettings.cs)
// runs every test method in this suite concurrently by default -- safe for
// every OTHER test in this class's own family (TicketExchangeHttpSqliteTests
// et al. only ever READ/validate a seeded client's credentials, never
// mutate the OpenIddict-registered application record itself), but this
// class's own tests all rotate "clinician-spa-client"'s secret in place --
// a genuine write-write race across parallel methods sharing one
// WebApplicationFactory/DbContext, confirmed by actually running this
// (ConcurrencyException, non-deterministically, only when run as a class).
[TestClass]
[DoNotParallelize]
public class TicketExchangeSecretRotationHttpSqliteTests
{
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
    }

    private static async Task RotateSecretAsync(string clientId, string newSecret, TimeSpan? overlapWindow = null)
    {
        var response = await _devIdpClient.PostAsJsonAsync($"/oauth/clients/{clientId}/rotate-secret", new { NewClientSecret = newSecret, OverlapWindow = overlapWindow });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // Mirrors TicketExchangeHttpSqliteTests' own private ExchangeForTicketAsync
    // helper (client_id path only -- rotation doesn't touch the one_time_secret
    // path at all, since that path never involves a registered client_secret).
    private static async Task<string> ExchangeForTicketAsync(string subjectToken, DpopKeyPair dpopKey, string clientId, string clientSecret)
    {
        var tokenUrl = new Uri(_devIdpClient.BaseAddress!, "/connect/token").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["requested_token_type"] = "urn:eventstore:token-type:ticket",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }),
        };
        request.Headers.Add("DPoP", dpopKey.CreateProof("POST", tokenUrl));

        var response = await _devIdpClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("ticket").GetString()!;
    }

    private static async Task<bool> IntrospectAsync(string ticket, string sig)
    {
        var response = await _devIdpClient.PostAsync("/oauth/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = ticket,
            ["sig"] = sig,
        }));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean();
    }

    // Every mutating test below shares ONE "clinician-spa-client" application
    // record across the whole class (ClassInitialize, not per-test setup),
    // so a test can never assume the seeded "clinician-spa-client-secret" is
    // still current -- an EARLIER test in the same run may have already
    // rotated it away. Establishing a known baseline via its OWN extra
    // rotation first (whatever the actual current secret happens to be,
    // moving to a fresh, test-owned value) makes each test's own assertions
    // deterministic regardless of run order or how many other tests ran
    // first -- confirmed necessary by actually running this: without it,
    // a later test's hardcoded "clinician-spa-client-secret" literal
    // intermittently failed with a real 401, not a test bug in isolation.
    private static async Task<string> EstablishKnownBaselineSecretAsync(string clientId)
    {
        var baseline = $"test-baseline-{Guid.NewGuid():N}";
        await RotateSecretAsync(clientId, baseline);
        return baseline;
    }

    [TestMethod]
    public async Task ARotatedClientCanStillAuthenticateWithThePreviousSecretDuringTheOverlapWindow()
    {
        const string clientId = "clinician-spa-client";
        var originalSecret = await EstablishKnownBaselineSecretAsync(clientId);
        var newSecret = $"rotated-secret-{Guid.NewGuid():N}";

        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, originalSecret, "attachments:read");

        await RotateSecretAsync(clientId, newSecret);

        // The caller hasn't picked up the new secret yet -- still presents
        // the OLD one on the token-exchange request itself. Without the
        // ValidateTokenRequestContext handler's own rewrite (Program.cs's
        // .AddServer registration), this would 400/401 invalid_client,
        // since OpenIddict's own registered application record now only
        // recognizes the NEW secret.
        var ticketWithOldSecret = await ExchangeForTicketAsync(subjectToken, key, clientId, originalSecret);
        Assert.IsFalse(string.IsNullOrEmpty(ticketWithOldSecret));

        // The NEW secret works too -- an ordinary, un-rotated credential
        // check, ValidateClientSecretAsync succeeding on its own.
        var ticketWithNewSecret = await ExchangeForTicketAsync(subjectToken, key, clientId, newSecret);
        Assert.IsFalse(string.IsNullOrEmpty(ticketWithNewSecret));
        Assert.AreNotEqual(ticketWithOldSecret, ticketWithNewSecret);
    }

    [TestMethod]
    public async Task IntrospectionAcceptsASignatureComputedWithThePreviousSecretDuringTheOverlapWindow()
    {
        const string clientId = "clinician-spa-client";
        var originalSecret = await EstablishKnownBaselineSecretAsync(clientId);
        var newSecret = $"rotated-secret-{Guid.NewGuid():N}";

        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, originalSecret, "attachments:read");
        // Ticket issuance itself still needs a currently-valid credential
        // on the exchange request -- issued with the NEW secret, simulating
        // a caller that already rotated its OWN copy for outbound calls,
        // but whose previously-cached signing secret (used for the sig it
        // computes on ITS OWN next step) hasn't been refreshed yet.
        await RotateSecretAsync(clientId, newSecret);
        var ticket = await ExchangeForTicketAsync(subjectToken, key, clientId, newSecret);

        // Signed with the OLD secret -- the exact overlap-window scenario
        // ADR-093's own Decision named: a ticket's whole lifecycle
        // (issuance -> signing -> resolution) completes well within any
        // real rotation window, but issuance and signing can straddle the
        // instant a caller's own credential refresh actually lands.
        var sigFromOldSecret = HmacSigner.Sign(ticket, originalSecret);
        Assert.IsTrue(await IntrospectAsync(ticket, sigFromOldSecret), "introspection must accept a signature computed against the previous secret while it's still within its overlap window");
    }

    [TestMethod]
    public async Task ASecretThatWasNeverCurrentOrPreviousIsStillRejectedAfterRotation()
    {
        const string clientId = "clinician-spa-client";
        var originalSecret = await EstablishKnownBaselineSecretAsync(clientId);
        var newSecret = $"rotated-secret-{Guid.NewGuid():N}";

        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, clientId, originalSecret, "attachments:read");
        await RotateSecretAsync(clientId, newSecret);

        var tokenUrl = new Uri(_devIdpClient.BaseAddress!, "/connect/token").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["requested_token_type"] = "urn:eventstore:token-type:ticket",
                ["client_id"] = clientId,
                ["client_secret"] = "never-a-real-secret",
            }),
        };
        request.Headers.Add("DPoP", key.CreateProof("POST", tokenUrl));

        var response = await _devIdpClient.SendAsync(request);
        // OpenIddict's own built-in ValidateClientSecret handler rejects
        // this one (401, ID2055) before this repo's own /connect/token
        // delegate code -- which returns its own 400 shape -- ever runs;
        // confirmed by actually running this rather than assumed from the
        // delegate's own (unreachable, for this exact case) 400 response.
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RotatingAnUnregisteredClientIdReturnsNotFound()
    {
        var response = await _devIdpClient.PostAsJsonAsync("/oauth/clients/never-registered-client/rotate-secret", new { NewClientSecret = "whatever" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
