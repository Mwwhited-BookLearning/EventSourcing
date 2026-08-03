using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Auth (OIDC/OpenIddict) + Orchestration" (docs/08-
// build-plan.md). Unlike every other item's ScenarioAssertions, this one
// drives real HTTP requests against two in-memory WebApplicationFactory
// TestServers (EventStore.DevIdp issuing real tokens, one EventStore.Host.*
// deployable validating them) rather than calling services directly --
// auth is pipeline/middleware behavior, only observable end to end. Only
// one provider's Host is exercised (Sqlite): the auth/CORS mechanism itself
// is provider-agnostic, unlike the JSON-path/SQL-translation code that
// genuinely needs all three providers tested.
internal static class AuthScenarioAssertions
{
    public static async Task<string> GetTokenAsync(HttpClient devIdpClient, string clientId, string clientSecret, string scope)
    {
        var response = await devIdpClient.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope,
        }));
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return body["access_token"]!.GetValue<string>();
    }

    public static async Task RequestWithoutAuthorizationHeaderIsRejected(HttpClient hostClient)
    {
        var response = await hostClient.PostAsync("/publish/whatever", JsonContent(new { }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static async Task RequestWithAnInvalidTokenIsRejected(HttpClient hostClient)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static async Task TokenMissingTheRequiredScopeIsRejectedWith403(HttpClient hostClient, HttpClient devIdpClient)
    {
        // follower-client's token carries events:follow/events:lineage:read, not events:publish.
        var token = await GetTokenAsync(devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, response.Headers.WwwAuthenticate.ToString());
    }

    public static async Task RegisteringAnEventTypeAndPublishingToItWithTheRightScopesSucceeds(HttpClient hostClient, HttpClient devIdpClient)
    {
        var operatorToken = await GetTokenAsync(devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
        const string schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";
        using (var registerRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/AuthDemoEvent")
        {
            Content = JsonContent(new
            {
                appId = "auth-demo",
                jsonSchema = schema,
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.Id",
                parentValidationMode = "Permissive",
            }),
        })
        {
            registerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
            var registerResponse = await hostClient.SendAsync(registerRequest);
            Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode, await registerResponse.Content.ReadAsStringAsync());
        }

        var publisherToken = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/AuthDemoEvent")
        {
            Content = JsonContent(new { appId = "auth-demo", schemaVersion = 1, payload = """{ "Amount": 1 }""" }),
        };
        publishRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publisherToken);
        var publishResponse = await hostClient.SendAsync(publishRequest);
        Assert.AreEqual(HttpStatusCode.Created, publishResponse.StatusCode, await publishResponse.Content.ReadAsStringAsync());
    }

    public static async Task RegistryPutWithoutRegistryAdminScopeIsRejectedWith403(HttpClient hostClient, HttpClient devIdpClient)
    {
        var publisherToken = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/SomeOtherType") { Content = JsonContent(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publisherToken);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static async Task OpenApiAndAsyncApiStayAnonymouslyReadable(HttpClient hostClient)
    {
        Assert.AreEqual(HttpStatusCode.OK, (await hostClient.GetAsync("/openapi.json")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await hostClient.GetAsync("/asyncapi.json")).StatusCode);
    }

    public static async Task AnAllowedOriginGetsCorsHeadersAndADisallowedOriginDoesNot(HttpClient hostClient, string allowedOrigin, string disallowedOrigin)
    {
        var allowed = await Preflight(hostClient, allowedOrigin);
        Assert.IsTrue(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedValues), "expected CORS headers for an allow-listed origin");
        Assert.AreEqual(allowedOrigin, allowedValues!.Single());

        var disallowed = await Preflight(hostClient, disallowedOrigin);
        Assert.IsFalse(disallowed.Headers.Contains("Access-Control-Allow-Origin"), "expected no CORS headers for an origin not on the allow-list");
    }

    private static async Task<HttpResponseMessage> Preflight(HttpClient hostClient, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/publish/whatever");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await hostClient.SendAsync(request);
    }

    private static HttpContent JsonContent(object value) =>
        System.Net.Http.Json.JsonContent.Create(value);
}
