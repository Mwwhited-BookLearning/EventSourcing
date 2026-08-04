extern alias DevIdpAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.Dpop;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;

namespace EventStore.IntegrationTests;

// "Ticket Exchange for Header-Incapable Clients" (docs/08-build-plan.md,
// ADR-040) -- a three-hop, cross-process flow (issuance at DevIdp,
// client-side signing, resolution at the receiving Host), only provably
// correct end to end, the same reasoning AuthScenarioAssertions' own HTTP-
// only test style already established. Uses Attachment retrieval
// (ADR-032) as the header-incapable target under test -- an <img src>/
// <a href> is exactly as valid a proof of ADR-040's own exit criteria as
// a <video src> would be (both are named, equally, as the two places a URL
// is genuinely the only transport); Streaming Channel byte-range playback
// shares the identical AuthorizeAttribute wiring and is not re-proven a
// second time here.
[TestClass]
public class TicketExchangeHttpSqliteTests
{
    private static string _dbPath = default!;
    private static WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory = default!;
    private static HttpClient _devIdpClient = default!;
    private static WebApplicationFactory<Program> _hostFactory = default!;
    private static HttpClient _hostClient = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-ticket-exchange-http-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using (var db = new EventStoreContext(options, new SqliteJsonPathTranslator()))
            await db.Database.MigrateAsync();

        _devIdpFactory = new WebApplicationFactory<DevIdpAssembly::Program>();
        _devIdpClient = _devIdpFactory.CreateClient();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(_devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        _hostFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
            builder.UseSetting("Authentication:Authority", _devIdpClient.BaseAddress!.ToString());
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                o.RequireHttpsMetadata = false;
            }));
            // TicketAuthenticationHandler's own introspection call goes out
            // via IHttpClientFactory.CreateClient() -- against a real
            // absolute URL (Authentication:Authority + "oauth/introspect"),
            // which needs to resolve to the SAME in-memory DevIdp
            // TestServer, not a real socket. Registering a matching
            // primary-handler override is the standard WebApplicationFactory
            // pattern for exactly this cross-TestServer call shape.
            builder.ConfigureServices(services => services
                .AddHttpClient(string.Empty)
                .ConfigurePrimaryHttpMessageHandler(() => _devIdpFactory.Server.CreateHandler()));
        });
        _hostClient = _hostFactory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _hostClient.Dispose();
        _hostFactory.Dispose();
        _devIdpClient.Dispose();
        _devIdpFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static async Task<string> UploadAttachmentAsync(byte[] bytes)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "attachments-client", "attachments-client-secret", "attachments:ingest");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/attachments")
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
        };
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contentHash").GetString()!;
    }

    // ADR-040 step 1, driven directly (AuthScenarioAssertions.GetTokenAsync
    // only knows the client_credentials shape) -- the requesting party's
    // own ordinary, DPoP-bound, header-based request. The client_id path
    // goes through the real RFC 8693 grant on /connect/token; the
    // one_time_secret path uses the separate /oauth/ticket-exchange
    // endpoint -- found only by actually running this against OpenIddict's
    // real pipeline, which unconditionally requires client_id for any
    // grant type reaching /connect/token, incompatible with a genuinely
    // clientless caller (see Program.cs's own comment on that endpoint).
    private static async Task<(string Ticket, int ExpiresIn)> ExchangeForTicketAsync(
        string subjectToken, DpopKeyPair dpopKey, string? clientId = null, string? clientSecret = null, string? oneTimeSecret = null)
    {
        HttpRequestMessage request;
        if (oneTimeSecret is not null)
        {
            var ticketExchangeUrl = new Uri(_devIdpClient.BaseAddress!, "/oauth/ticket-exchange").ToString();
            request = new HttpRequestMessage(HttpMethod.Post, "/oauth/ticket-exchange")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["subject_token"] = subjectToken,
                    ["one_time_secret"] = oneTimeSecret,
                }),
            };
            request.Headers.Add("DPoP", dpopKey.CreateProof("POST", ticketExchangeUrl));
        }
        else
        {
            var tokenUrl = new Uri(_devIdpClient.BaseAddress!, "/connect/token").ToString();
            request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                    ["subject_token"] = subjectToken,
                    ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["requested_token_type"] = "urn:eventstore:token-type:ticket",
                    ["client_id"] = clientId!,
                    ["client_secret"] = clientSecret!,
                }),
            };
            request.Headers.Add("DPoP", dpopKey.CreateProof("POST", tokenUrl));
        }

        using (request)
        {
            var response = await _devIdpClient.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (body.GetProperty("ticket").GetString()!, body.GetProperty("expiresIn").GetInt32());
        }
    }

    private static HttpRequestMessage HeaderIncapableRetrieveRequest(string contentHash, string ticket, string sig) =>
        new(HttpMethod.Get, $"/attachments/{contentHash}?ticket={Uri.EscapeDataString(ticket)}&sig={Uri.EscapeDataString(sig)}");

    [TestMethod]
    public async Task ATicketIssuedSignedAndResolvedSuccessfullyRetrievesContentWithNoAuthorizationHeaderAtAll()
    {
        var bytes = "the actual bytes a <img src>/<a href> would fetch"u8.ToArray();
        var contentHash = await UploadAttachmentAsync(bytes);

        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "attachments:read");
        var (ticket, expiresIn) = await ExchangeForTicketAsync(subjectToken, key, clientId: "clinician-spa-client", clientSecret: "clinician-spa-client-secret");
        Assert.IsTrue(expiresIn > 0);

        var sig = HmacSigner.Sign(ticket, "clinician-spa-client-secret");

        // No Authorization header at all -- the entire point of this mechanism.
        using var request = HeaderIncapableRetrieveRequest(contentHash, ticket, sig);
        var response = await _hostClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEqual(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task TheSameTicketPresentedASecondTimeIsRejectedEvenBeforeExpiry()
    {
        var contentHash = await UploadAttachmentAsync("single-use content"u8.ToArray());
        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "attachments:read");
        var (ticket, _) = await ExchangeForTicketAsync(subjectToken, key, clientId: "clinician-spa-client", clientSecret: "clinician-spa-client-secret");
        var sig = HmacSigner.Sign(ticket, "clinician-spa-client-secret");

        using var first = HeaderIncapableRetrieveRequest(contentHash, ticket, sig);
        var firstResponse = await _hostClient.SendAsync(first);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);

        using var second = HeaderIncapableRetrieveRequest(contentHash, ticket, sig);
        var secondResponse = await _hostClient.SendAsync(second);
        Assert.AreEqual(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
    }

    [TestMethod]
    public async Task ATicketPresentedWithASignatureComputedFromTheWrongSecretIsRejectedBeforeAnyContentIsServed()
    {
        var contentHash = await UploadAttachmentAsync("wrong-secret content"u8.ToArray());
        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "attachments:read");
        var (ticket, _) = await ExchangeForTicketAsync(subjectToken, key, clientId: "clinician-spa-client", clientSecret: "clinician-spa-client-secret");

        var wrongSig = HmacSigner.Sign(ticket, "not-the-real-secret");
        using var wrongRequest = HeaderIncapableRetrieveRequest(contentHash, ticket, wrongSig);
        var wrongResponse = await _hostClient.SendAsync(wrongRequest);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

        // A wrong-signature presentation must NOT burn the ticket -- the
        // rightful owner's own later, correctly-signed retry still succeeds
        // (ADR-040's own distinction between the two threats single-use
        // consumption and the signature each separately bound).
        var correctSig = HmacSigner.Sign(ticket, "clinician-spa-client-secret");
        using var correctRequest = HeaderIncapableRetrieveRequest(contentHash, ticket, correctSig);
        var correctResponse = await _hostClient.SendAsync(correctRequest);
        Assert.AreEqual(HttpStatusCode.OK, correctResponse.StatusCode);
    }

    [TestMethod]
    public async Task AOneTimeSecretTicketNeverRequiresARegisteredClientId()
    {
        var contentHash = await UploadAttachmentAsync("one-time-secret content"u8.ToArray());
        var (subjectToken, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "clinician-spa-client", "clinician-spa-client-secret", "attachments:read");

        var oneTimeSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var (ticket, _) = await ExchangeForTicketAsync(subjectToken, key, oneTimeSecret: oneTimeSecret);
        var sig = HmacSigner.Sign(ticket, oneTimeSecret);

        using var request = HeaderIncapableRetrieveRequest(contentHash, ticket, sig);
        var response = await _hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // Exercises TicketStore's own expiry mechanics directly rather than
    // waiting out a real 60-second TTL over HTTP -- the same "exercise the
    // mechanics directly" pattern this repo's own time-dependent tests
    // (PendingJoinState's TTL sweep, upcast materialization backlog) always
    // use instead of a real clock delay.
    [TestMethod]
    public void AnExpiredTicketIsRejectedEvenIfNeverPresented()
    {
        var store = new DevIdpAssembly::EventStore.DevIdp.TicketStore();
        var expired = new DevIdpAssembly::EventStore.DevIdp.Ticket(
            "expired-ticket-value", "clinician-spa-client", DateTimeOffset.UtcNow.AddSeconds(-1), []);
        store.Add(expired);

        Assert.IsFalse(store.TryGet("expired-ticket-value", out _));
    }

    [TestMethod]
    public async Task AnOrdinaryBearerAuthenticatedRequestToTheSameRouteIsCompletelyUnaffected()
    {
        var bytes = "ordinary bearer retrieval, unaffected by ADR-040"u8.ToArray();
        var contentHash = await UploadAttachmentAsync(bytes);

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(_devIdpClient, "attachments-client", "attachments-client-secret", "attachments:read");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/attachments/{contentHash}");
        AuthScenarioAssertions.AttachAuth(request, _hostClient, token, key);
        var response = await _hostClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(bytes, await response.Content.ReadAsByteArrayAsync());
    }
}
