using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // ADR-010 -- mode: Replay, fromSequenceNumber: <checkpoint> always, per
    // ADR-015's "always replay, never tail" (no reason to track two code
    // paths for "starting fresh" vs. "resuming"). A fresh token is acquired
    // per call -- a long-lived SSE connection's bearer token is only ever
    // checked at the initial request, so no mid-stream refresh is needed.
    public async IAsyncEnumerable<FollowedEventEnvelope> TailAsync(
        string eventTypeName, string appId, long fromSequenceNumber, [EnumeratorCancellation] CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var followClient = httpClientFactory.CreateClient("Follow");

        using var request = new HttpRequestMessage(QueryMethod, $"/follow/{eventTypeName}")
        {
            Content = JsonContent.Create(new { appId, mode = "Replay", fromSequenceNumber }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
        var token = await GetAccessTokenAsync(ct);
        var followClient = httpClientFactory.CreateClient("Follow");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/registry/{eventTypeName}/change-kind");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await followClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return Enum.Parse<ChangeKind>(body["changeKind"]!.GetValue<string>());
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var devIdpClient = httpClientFactory.CreateClient("DevIdp");
        var response = await devIdpClient.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.Value.ClientId,
            ["client_secret"] = options.Value.ClientSecret,
            ["scope"] = options.Value.Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return body["access_token"]!.GetValue<string>();
    }
}
