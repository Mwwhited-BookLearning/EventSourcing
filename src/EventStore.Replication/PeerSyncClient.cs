using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Dpop;
using Microsoft.Extensions.Options;

namespace EventStore.Replication;

// ADR-033 -- "client->server, server->server, and server->client are
// three relationships over one mechanism, not three": this client plays
// the identical role FollowClient/publish clients already play, just
// dialing another site instead of this site's own write/read APIs. One
// DPoP key pair per process, the same posture every other real client in
// this repo already uses (ADR-017).
public class PeerSyncClient(IHttpClientFactory httpClientFactory, IOptions<PeerSyncClientOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DpopKeyPair _keyPair = DpopKeyPair.Generate();

    public async Task<(string PeerId, string? Region)> WhoAmIAsync(string address, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PeerSync");
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{address}/peer-sync/whoami");
        AttachAuth(request, client, token);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return (body["originId"]!.GetValue<string>(), body["region"]?.GetValue<string>());
    }

    public async Task<PeerSyncPushResponse> PushAsync(string address, PeerSyncPushRequest request, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PeerSync");
        var token = await GetAccessTokenAsync(ct);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{address}/peer-sync/push")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        AttachAuth(httpRequest, client, token);

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PeerSyncPushResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("empty /peer-sync/push response");
    }

    private void AttachAuth(HttpRequestMessage request, HttpClient client, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // RFC 9449 -- htu excludes the query string/fragment (the same
        // fixed latent bug already found for every other real client in
        // this repo, ADR-013). RequestUri may be relative when the caller
        // targets a client whose BaseAddress supplies the real host (the
        // same FixedHttpClientFactory pattern FollowClient already uses).
        var absoluteUri = request.RequestUri!.IsAbsoluteUri ? request.RequestUri : new Uri(client.BaseAddress!, request.RequestUri!);
        var htu = absoluteUri.GetLeftPart(UriPartial.Path);
        request.Headers.Add("DPoP", _keyPair.CreateProof(request.Method.Method, htu, token));
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
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
        request.Headers.Add("DPoP", _keyPair.CreateProof("POST", tokenUrl));

        var response = await devIdpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return body["access_token"]!.GetValue<string>();
    }
}
