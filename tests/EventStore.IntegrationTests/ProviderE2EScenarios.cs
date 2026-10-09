using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// The real EventStore.Host.<Provider> (real DevIdp tokens + DPoP, real
// background workers, real container database) driven only over HTTP. The
// identical scenario set runs once per provider via the two derived classes,
// so a provider-specific divergence shows up as one provider failing a test
// the other passes.
public abstract class ProviderE2EScenarios
{
    protected abstract ProviderE2EHarness Harness { get; }

    private static readonly HttpMethod Query = new("QUERY");

    private static string NewAppId() => $"e2e-{Guid.NewGuid():N}"[..16];

    private async Task RegisterAsync(string eventType, string appId)
    {
        const string schema = """{ "type": "object", "properties": { "Id": { "type": "string" }, "Amount": { "type": "number" } }, "required": ["Id"] }""";
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(Harness.DevIdp, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/registry/{eventType}")
        {
            Content = JsonContent.Create(new { appId, jsonSchema = schema, filterableFields = Array.Empty<object>(), changeKind = "Full", entityIdField = "$.Id" }),
        };
        AuthScenarioAssertions.AttachAuth(request, Harness.Host, token, key);
        var response = await Harness.Host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PublishAsync(string eventType, string appId, string id, Guid? eventId = null, Guid[]? parents = null, int amount = 1)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(Harness.DevIdp, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publish/{eventType}")
        {
            Content = JsonContent.Create(new { appId, schemaVersion = 1, payload = $$"""{ "Id": "{{id}}", "Amount": {{amount}} }""", eventId, parentEventIds = parents }),
        };
        AuthScenarioAssertions.AttachAuth(request, Harness.Host, token, key);
        return await Harness.Host.SendAsync(request);
    }

    private async Task<JsonElement> GraphQlAsync(string query)
    {
        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(Harness.DevIdp, "follower-client", "follower-client-secret", "events:lineage:read");
        using var request = new HttpRequestMessage(Query, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json"),
        };
        AuthScenarioAssertions.AttachAuth(request, Harness.Host, token, key);
        var response = await Harness.Host.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.TryGetProperty("errors", out _), body.ToString());
        return body.GetProperty("data");
    }

    [TestMethod]
    public async Task PublishedEventIsReadableByIdOverGraphQl()
    {
        var appId = NewAppId();
        var eventType = $"E2eRead{appId.Replace("-", "")}";
        await RegisterAsync(eventType, appId);
        var eventId = Guid.NewGuid();
        var response = await PublishAsync(eventType, appId, "r-1", eventId);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());

        var data = await GraphQlAsync($$"""query { event(eventId: "{{eventId}}") { eventId } }""");
        Assert.AreEqual(eventId, data.GetProperty("event").GetProperty("eventId").GetGuid());
    }

    [TestMethod]
    public async Task ReplayingTheSameEventIdWithTheSameContentIsIdempotentAndDifferentContentConflicts()
    {
        var appId = NewAppId();
        var eventType = $"E2eIdem{appId.Replace("-", "")}";
        await RegisterAsync(eventType, appId);
        var eventId = Guid.NewGuid();

        Assert.AreEqual(HttpStatusCode.Accepted, (await PublishAsync(eventType, appId, "i-1", eventId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await PublishAsync(eventType, appId, "i-1", eventId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await PublishAsync(eventType, appId, "i-1", eventId, amount: 99)).StatusCode);
    }

    [TestMethod]
    public async Task PublishingAnUnregisteredEventTypeIsRejectedAsNotFound()
    {
        var response = await PublishAsync($"NeverRegistered{Guid.NewGuid():N}", NewAppId(), "x-1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ChildEventExposesItsParentThroughTheLineageApi()
    {
        var appId = NewAppId();
        var eventType = $"E2eLineage{appId.Replace("-", "")}";
        await RegisterAsync(eventType, appId);
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        Assert.AreEqual(HttpStatusCode.Accepted, (await PublishAsync(eventType, appId, "l-1", parent)).StatusCode);
        var c = await PublishAsync(eventType, appId, "l-1", child, [parent]);
        Assert.AreEqual(HttpStatusCode.Accepted, c.StatusCode, await c.Content.ReadAsStringAsync());

        var data = await GraphQlAsync($$"""query { event(eventId: "{{child}}") { parents { eventId resolved restricted } } }""");
        var parents = data.GetProperty("event").GetProperty("parents");
        Assert.AreEqual(1, parents.GetArrayLength());
        Assert.AreEqual(parent, parents[0].GetProperty("eventId").GetGuid());
    }

    [TestMethod]
    public async Task HashChainVerifiesCleanAfterConcurrentPublishes()
    {
        var appId = NewAppId();
        var eventType = $"E2eChain{appId.Replace("-", "")}";
        await RegisterAsync(eventType, appId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => PublishAsync(eventType, appId, $"c-{i}")));
        var failed = responses.FirstOrDefault(r => r.StatusCode != HttpStatusCode.Accepted);
        Assert.IsNull(failed, failed is null ? null : $"{(int)failed.StatusCode}: {await failed.Content.ReadAsStringAsync()}");

        var (token, key) = await AuthScenarioAssertions.GetTokenAsync(Harness.DevIdp, "operator-client", "operator-client-secret", "registry:admin");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/events/verify?throughSequenceNumber={long.MaxValue}");
        AuthScenarioAssertions.AttachAuth(request, Harness.Host, token, key);
        var response = await Harness.Host.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(body.GetProperty("verified").GetBoolean(), body.ToString());
    }
}
