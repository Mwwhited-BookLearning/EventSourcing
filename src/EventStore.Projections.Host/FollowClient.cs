using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Dpop;
using Microsoft.Extensions.Options;

namespace EventStore.Projections.Host;

// docs/06-solution-structure.md: ProjectionHost's only dependency on the
// write side is an HTTP client calling QUERY /follow/{event-type} -- this is
// that client. Named HttpClients ("Follow", "DevIdp") so a caller (Program.cs
// or a test) can point each at a real WebApplicationFactory TestServer client
// or a real network base address identically.
public class FollowClient(IHttpClientFactory httpClientFactory, IOptions<FollowClientOptions> options)
{
    private static readonly HttpMethod QueryMethod = new("QUERY");
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // ADR-017 -- "each of the four OAuth2 clients generates its own
    // asymmetric key pair." projections-client is the one client this repo
    // actually implements as real, running code (every other seeded client
    // is only ever driven directly from test code) -- so it's this class,
    // not a seeder, that generates and holds the key.
    //
    // Generated FRESH per call (TailAsync/GetChangeKindAsync), not held as
    // one shared instance field -- FollowClient is a DI singleton, and
    // RbacProjectionWorker (ADR-067) calls TailAsync several times
    // concurrently off the SAME instance, one per (AppId, event type). A
    // shared ECDsa/DpopKeyPair used concurrently from multiple threads hung
    // indefinitely inside CreateToken (found only by running this -- no
    // exception, no timeout, reproducible). Nothing in RFC 9449 requires
    // reusing one key across unrelated token-acquisition + request-proof
    // pairs; each TailAsync call already acquires its OWN access token via
    // GetAccessTokenAsync, so giving it its own key too, used only for that
    // token and the proofs presenting it, is a correct fix, not a
    // workaround around the binding semantics.
    // ADR-010 -- mode: Replay, fromSequenceNumber: <checkpoint> always, per
    // ADR-015's "always replay, never tail" (no reason to track two code
    // paths for "starting fresh" vs. "resuming"). A fresh token is acquired
    // per call -- a long-lived SSE connection's bearer token is only ever
    // checked at the initial request, so no mid-stream refresh is needed.
    public async IAsyncEnumerable<FollowedEventEnvelope> TailAsync(
        string eventTypeName, string appId, long fromSequenceNumber, [EnumeratorCancellation] CancellationToken ct)
    {
        var keyPair = DpopKeyPair.Generate();
        var token = await GetAccessTokenAsync(keyPair, ct);
        var followClient = httpClientFactory.CreateClient("Follow");

        // A hand-built JSON string, not JsonContent.Create(new { ... }) --
        // System.Text.Json's reflection-based metadata resolution for a
        // fresh anonymous type, invoked concurrently from several of
        // RbacProjectionWorker's tail loops at once, hung indefinitely the
        // first time it ran (found only by running this). appId is the only
        // piece here that needs proper JSON-string escaping.
        var requestJson = $$"""{"appId": {{JsonSerializer.Serialize(appId)}}, "mode": "Replay", "fromSequenceNumber": {{fromSequenceNumber}}}""";
        using var request = new HttpRequestMessage(QueryMethod, $"/follow/{eventTypeName}")
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json"),
        };
        AttachAuth(request, followClient, keyPair, token);

        using var response = await followClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        // A RequiredClaims Read-direction gate this client's own token doesn't
        // satisfy (ADR-008/050) means this event type is simply never visible to
        // this projection -- not a failure that should crash the whole
        // ProjectionHost or its other, unrelated event-type connections.
        if (response.StatusCode == HttpStatusCode.Forbidden)
            yield break;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                yield break; // stream ended (connection closed) -- ProjectionHost's own loop reconnects
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue; // blank separator lines between SSE events

            yield return JsonSerializer.Deserialize<FollowedEventEnvelope>(line["data: ".Length..], EnvelopeJsonOptions)!;
        }
    }

    // ADR-016 -- ChangeKind isn't carried on the per-event SSE envelope itself
    // (it's a property of the event TYPE's registration, not the event), so
    // ProjectionHost fetches it separately via the same HTTP-only path.
    public async Task<ChangeKind> GetChangeKindAsync(string eventTypeName, CancellationToken ct)
    {
        var keyPair = DpopKeyPair.Generate();
        var token = await GetAccessTokenAsync(keyPair, ct);
        var followClient = httpClientFactory.CreateClient("Follow");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/registry/{eventTypeName}/change-kind");
        AttachAuth(request, followClient, keyPair, token);

        using var response = await followClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return Enum.Parse<ChangeKind>(body["changeKind"]!.GetValue<string>());
    }

    private static void AttachAuth(HttpRequestMessage request, HttpClient followClient, DpopKeyPair keyPair, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var absoluteUri = new Uri(followClient.BaseAddress!, request.RequestUri!);
        // RFC 9449 -- htu excludes the query string and fragment; neither of
        // this client's own requests carries one today, but the resource
        // server's own DpopValidationMiddleware computes expectedHtu from
        // HttpRequest.Path alone, so this must match exactly if one is ever added.
        var htu = absoluteUri.GetLeftPart(UriPartial.Path);
        request.Headers.Add("DPoP", keyPair.CreateProof(request.Method.Method, htu, token));
    }

    private async Task<string> GetAccessTokenAsync(DpopKeyPair keyPair, CancellationToken ct)
    {
        var devIdpClient = httpClientFactory.CreateClient("DevIdp");
        var tokenUrl = new Uri(devIdpClient.BaseAddress!, "/connect/token").ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.ClientSecret,
                ["scope"] = options.Value.Scope,
            }),
        };
        request.Headers.Add("DPoP", keyPair.CreateProof("POST", tokenUrl));

        var response = await devIdpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return body["access_token"]!.GetValue<string>();
    }
}
