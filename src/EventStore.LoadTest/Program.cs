using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.DevIdp;
using EventStore.Dpop;

// Concurrent load against one running EventStore host.
//   dotnet run --project src/EventStore.LoadTest -- <hostBaseUrl> [publishes=3000] [concurrency=64] [eventTypes=6] [devIdpBaseUrl=http://localhost:5010] [routeTimeoutSeconds=60]
// Registers event types (also mid-burst), publishes a burst, then checks: every publish 202, the hash
// chain verifies, and routed entities become queryable. Exit code 1 on any failed check.
var host = new Uri(args.Length > 0 ? args[0] : throw new ArgumentException("host base URL required"));
var publishes = args.Length > 1 ? int.Parse(args[1]) : 3000;
var concurrency = args.Length > 2 ? int.Parse(args[2]) : 64;
var typeCount = args.Length > 3 ? int.Parse(args[3]) : 6;
var devIdp = new Uri(args.Length > 4 ? args[4] : "http://localhost:5010");
var routeTimeoutSeconds = args.Length > 5 ? int.Parse(args[5]) : 60;

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = concurrency * 2 }) { Timeout = TimeSpan.FromMinutes(10) };
var tokens = new Dictionary<string, string>();
var keys = new ConcurrentDictionary<string, DpopKeyPair>();

async Task<string> TokenAsync(string client, string scope)
{
    var key = keys.GetOrAdd(client, _ => DpopKeyPair.Generate());
    var tokenUrl = new Uri(devIdp, "/connect/token");
    using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client,
            ["client_secret"] = DevIdpSeeder.GetClientSecret(client)!,
            ["scope"] = scope,
        }),
    };
    request.Headers.Add("DPoP", key.CreateProof("POST", tokenUrl.ToString()));
    var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<JsonNode>())!["access_token"]!.GetValue<string>();
}

async Task<HttpResponseMessage> SendAsync(string client, HttpMethod method, string path, string? json = null)
{
    var key = keys.GetOrAdd(client, _ => DpopKeyPair.Generate());
    var token = tokens[client];
    var uri = new Uri(host, path);
    using var request = new HttpRequestMessage(method, uri) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Add("DPoP", key.CreateProof(method.Method, uri.GetLeftPart(UriPartial.Path), token));
    return await http.SendAsync(request);
}

tokens["operator-client"] = await TokenAsync("operator-client", "registry:admin");
tokens["publisher-client"] = await TokenAsync("publisher-client", "events:publish");
tokens["follower-client"] = await TokenAsync("follower-client", "events:follow events:lineage:read");

var run = Guid.NewGuid().ToString("N")[..8];
var appId = $"load{run}";
const string schema = """{ "type": "object", "properties": { "Id": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["Id"] }""";
var failures = new List<string>();

async Task RegisterAsync(string eventType)
{
    var body = JsonSerializer.Serialize(new { appId, jsonSchema = schema, filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.Id", entityType = "widget" });
    var response = await SendAsync("operator-client", HttpMethod.Put, $"/registry/{eventType}", body);
    if (response.StatusCode != HttpStatusCode.Created)
        { var text = await response.Content.ReadAsStringAsync(); lock (failures) failures.Add($"register {eventType}: {(int)response.StatusCode} {text}"); }
}

var types = Enumerable.Range(0, typeCount).Select(i => $"Load{run}T{i}").ToArray();
var sw = Stopwatch.StartNew();
await Task.WhenAll(types.Select(RegisterAsync));
Console.WriteLine($"registered {typeCount} types concurrently in {sw.ElapsedMilliseconds} ms");

var statuses = new ConcurrentDictionary<string, int>();
var latencies = new ConcurrentBag<long>();
var ids = new ConcurrentBag<string>();
using var gate = new SemaphoreSlim(concurrency);
var lateTypes = Enumerable.Range(0, 4).Select(i => $"Load{run}Late{i}").ToArray();
var lateEvery = publishes / (lateTypes.Length + 1) + 1;

sw.Restart();
var work = Enumerable.Range(0, publishes).Select(async i =>
{
    await gate.WaitAsync();
    try
    {
        // Mid-burst registrations: a few new types land while publishes are in flight.
        if (i > 0 && i % lateEvery == 0 && i / lateEvery <= lateTypes.Length)
            await RegisterAsync(lateTypes[(i / lateEvery) - 1]);
        var id = $"w-{run}-{i}";
        var eventType = types[i % types.Length];
        var t0 = Stopwatch.GetTimestamp();
        var response = await SendAsync("publisher-client", HttpMethod.Post, $"/publish/{eventType}",
            JsonSerializer.Serialize(new { appId, schemaVersion = 1, payload = $$"""{ "Id": "{{id}}", "Amount": {{i}} }""" }));
        latencies.Add(Stopwatch.GetElapsedTime(t0).Ticks);
        statuses.AddOrUpdate($"{(int)response.StatusCode}", 1, (_, c) => c + 1);
        if (response.StatusCode == HttpStatusCode.Accepted)
            ids.Add(id);
        else
        {
            var text = await response.Content.ReadAsStringAsync();
            lock (failures) { if (failures.Count < 20) failures.Add($"publish {id}: {(int)response.StatusCode} {text}"); }
        }
    }
    catch (Exception ex)
    {
        statuses.AddOrUpdate("exception", 1, (_, c) => c + 1);
        lock (failures) { if (failures.Count < 20) failures.Add($"publish {i}: {ex.GetType().Name} {ex.Message}"); }
    }
    finally { gate.Release(); }
}).ToArray();
await Task.WhenAll(work);
sw.Stop();

var sorted = latencies.Select(t => TimeSpan.FromTicks(t).TotalMilliseconds).Order().ToArray();
if (sorted.Length > 0)
    Console.WriteLine($"{publishes} publishes @ concurrency {concurrency} in {sw.Elapsed.TotalSeconds:F1}s = {publishes / sw.Elapsed.TotalSeconds:F0}/s; p50 {sorted[sorted.Length / 2]:F0} ms, p99 {sorted[(int)(sorted.Length * 0.99)]:F0} ms");
Console.WriteLine("statuses: " + string.Join(", ", statuses.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
if (statuses.Keys.Any(k => k != "202"))
    failures.Add("non-202 publish responses (none expected: every id is unique)");

// Hash chain.
bool verified;
string verifyBody;
try
{
    var verify = await SendAsync("operator-client", HttpMethod.Get, $"/events/verify?throughSequenceNumber={long.MaxValue}");
    verifyBody = await verify.Content.ReadAsStringAsync();
    verified = verify.IsSuccessStatusCode && JsonNode.Parse(verifyBody)!["verified"]!.GetValue<bool>();
}
catch (Exception ex)
{
    verifyBody = $"{ex.GetType().Name}: {ex.Message}";
    verified = false;
}
Console.WriteLine($"verify: {(verified ? "clean" : "FAILED " + verifyBody)}");
if (!verified)
    failures.Add($"verify: {verifyBody}");

// Routed entities: sample, polling up to routeTimeoutSeconds for the RouterWorker to fold them in.
var sample = ids.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(200, ids.Count)).ToArray();
var pending = new HashSet<string>(sample);
var deadline = DateTime.UtcNow.AddSeconds(routeTimeoutSeconds);
string lastBody = "";
sw.Restart();
while (pending.Count > 0 && DateTime.UtcNow < deadline)
{
    var batch = pending.ToArray();
    var query = "query {" + string.Join(" ", batch.Select((id, i) => $"e{i}: entity_{appId}_widget(id: \"{id}\") {{ isAuthoritative }}")) + " }";
    var response = await SendAsync("follower-client", new HttpMethod("QUERY"), "/graphql", JsonSerializer.Serialize(new { query }));
    var rawBody = await response.Content.ReadAsStringAsync();
    lastBody = rawBody;
    var body = JsonNode.Parse(rawBody);
    if (body?["data"] is JsonObject data)
    {
        for (var i = 0; i < batch.Length; i++)
        {
            if (data[$"e{i}"] is JsonObject)
                pending.Remove(batch[i]);
        }
    }
    if (pending.Count > 0)
        await Task.Delay(500);
}
Console.WriteLine($"entities: {sample.Length - pending.Count}/{sample.Length} sampled routed (last arrival {sw.Elapsed.TotalSeconds:F1}s after publishes ended)");
if (pending.Count > 0)
    failures.Add($"{pending.Count} sampled entities never routed within {routeTimeoutSeconds}s, e.g. {pending.First()}; last GraphQL response: {(lastBody.Length > 600 ? lastBody[..600] : lastBody)}");

foreach (var f in failures.Take(20))
    Console.WriteLine("FAIL " + f);
return failures.Count == 0 ? 0 : 1;
