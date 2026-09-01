extern alias DevIdpAssembly;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EventStore.Dpop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevIdpSeeder = DevIdpAssembly::EventStore.DevIdp.DevIdpSeeder;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Auth (OIDC/OpenIddict) + Orchestration" (docs/08-
// build-plan.md) and, since this item's own item, "Hardening & Evolution"'s
// DPoP sub-part (ADR-017). Unlike every other item's ScenarioAssertions,
// this one drives real HTTP requests against two in-memory
// WebApplicationFactory TestServers (EventStore.DevIdp issuing real tokens,
// one EventStore.Host.* deployable validating them) rather than calling
// services directly -- auth is pipeline/middleware behavior, only
// observable end to end. Only one provider's Host is exercised (Sqlite):
// the auth/CORS/DPoP mechanism itself is provider-agnostic, unlike the
// JSON-path/SQL-translation code that genuinely needs all three providers
// tested.
internal static class AuthScenarioAssertions
{
    // Every seeded client's key pair, per ADR-017 ("each of the four OAuth2
    // clients generates its own asymmetric key pair") -- DevIdpSeeder plays
    // the client's role too, since no separate client process exists in
    // this repo (see that seeder's own comment).
    public static async Task<(string Token, DpopKeyPair Key)> GetTokenAsync(
        HttpClient devIdpClient, string clientId, string clientSecret, string scope, string? appId = null, string? acr = null)
    {
        var key = DevIdpSeeder.GetClientKeyPair(clientId);
        var tokenUrl = new Uri(devIdpClient.BaseAddress!, "/connect/token").ToString();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope,
        };
        // "Delegated Grants, RBAC, Federated Claims" (ADR-046) -- opt-in
        // only: every pre-existing call site never passes appId and is
        // completely unaffected.
        if (appId is not null)
            form["app_id"] = appId;
        // "Digital Sign-Off" (ADR-066) -- this dev/POC IdP's own step-up
        // simulation, same opt-in shape as app_id above: a real IdP would
        // only ever grant a given acr after actually performing that
        // authentication method, this one just takes the caller's word for
        // it (Program.cs's own comment explains why).
        if (acr is not null)
            form["acr"] = acr;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Add("DPoP", key.CreateProof("POST", tokenUrl));

        var response = await devIdpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (body["access_token"]!.GetValue<string>(), key);
    }

    // Attaches both the bearer token and a fresh, per-request DPoP proof
    // (RFC 9449 -- never reused across requests, unlike the token itself)
    // bound to this exact method/URI and to the token being presented.
    public static void AttachAuth(HttpRequestMessage request, HttpClient hostClient, string token, DpopKeyPair key)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var absoluteUri = new Uri(hostClient.BaseAddress!, request.RequestUri!);
        // RFC 9449 -- htu excludes the query string and fragment; DpopValidationMiddleware's
        // own expectedHtu is built from HttpRequest.Path alone, never Path+QueryString.
        var htu = absoluteUri.GetLeftPart(UriPartial.Path);
        request.Headers.Add("DPoP", key.CreateProof(request.Method.Method, htu, token));
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
        var (token, key) = await GetTokenAsync(devIdpClient, "follower-client", "follower-client-secret", "events:follow");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        AttachAuth(request, hostClient, token, key);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, response.Headers.WwwAuthenticate.ToString());
    }

    public static async Task RegisteringAnEventTypeAndPublishingToItWithTheRightScopesSucceeds(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (operatorToken, operatorKey) = await GetTokenAsync(devIdpClient, "operator-client", "operator-client-secret", "registry:admin");
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
            AttachAuth(registerRequest, hostClient, operatorToken, operatorKey);
            var registerResponse = await hostClient.SendAsync(registerRequest);
            Assert.AreEqual(HttpStatusCode.Created, registerResponse.StatusCode, await registerResponse.Content.ReadAsStringAsync());
        }

        var (publisherToken, publisherKey) = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/publish/AuthDemoEvent")
        {
            Content = JsonContent(new { appId = "auth-demo", schemaVersion = 1, payload = """{ "Amount": 1 }""" }),
        };
        AttachAuth(publishRequest, hostClient, publisherToken, publisherKey);
        var publishResponse = await hostClient.SendAsync(publishRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, publishResponse.StatusCode, await publishResponse.Content.ReadAsStringAsync());
    }

    public static async Task RegistryPutWithoutRegistryAdminScopeIsRejectedWith403(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (publisherToken, publisherKey) = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/SomeOtherType") { Content = JsonContent(new { }) };
        AttachAuth(request, hostClient, publisherToken, publisherKey);
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

    // docs/bugs/framework/service/devidp-token-endpoint-missing-standalone-cors-origin.md --
    // DevIdp's own /connect/token endpoint has the identical config-driven
    // CORS mechanism as any EventStore.Host.* (Program.cs's own comment on
    // CorsBeforeOpenIddictStartupFilter), but its Development appsettings
    // never populated Cors:AllowedOrigins with the standalone-dev origin
    // Host.Sqlite's own Development appsettings already carries -- found by
    // actually running client-web standalone (`npm run dev:vitals`, no
    // AppHost) against a manually-started DevIdp and getting a real
    // "blocked by CORS policy" browser console error on the token request.
    public static async Task DevIdpTokenEndpointGetsCorsHeadersForAnAllowedOrigin(HttpClient devIdpClient, string allowedOrigin, string disallowedOrigin)
    {
        var allowed = await Preflight(devIdpClient, allowedOrigin, "/connect/token");
        Assert.IsTrue(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedValues), "expected CORS headers for an allow-listed origin on DevIdp's own token endpoint");
        Assert.AreEqual(allowedOrigin, allowedValues!.Single());

        var disallowed = await Preflight(devIdpClient, disallowedOrigin, "/connect/token");
        Assert.IsFalse(disallowed.Headers.Contains("Access-Control-Allow-Origin"), "expected no CORS headers for an origin not on DevIdp's own allow-list");
    }

    // docs/bugs/framework/service/devidp-duplicate-typed-extra-claims-collapse.md --
    // DevIdpSeeder.ExtraClaims lets one client hold more than one claim of
    // the SAME type (vitals-pi-client's own "review":"ae"/"review":"ionm"
    // pair) -- Program.cs's token-issuance loop used to call
    // identity.SetClaim(type, value) per tuple, which REPLACES any existing
    // claim of that type rather than adding to it, so only the last-looped
    // value of a repeated type ever survived into the issued token. Found
    // by decoding a REAL vitals-pi-client token's own JWT payload (not by
    // reading the seeding code back) while live-verifying the flow engine's
    // myTasks GraphQL query against a real browser: the payload showed only
    // "review":"ionm", never "ae", even though DevIdpSeeder.ExtraClaims
    // lists both.
    public static async Task AClientSeededWithMultipleSameTypedExtraClaimsGetsAllOfThemInTheIssuedToken(HttpClient devIdpClient)
    {
        var (token, _) = await GetTokenAsync(devIdpClient, "vitals-pi-client", "vitals-pi-client-secret", "events:publish");
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

        Assert.IsTrue(claims.Any(c => c.Type == "review" && c.Value == "ae"), "expected the issued token to still carry review:ae alongside review:ionm");
        Assert.IsTrue(claims.Any(c => c.Type == "review" && c.Value == "ionm"), "expected the issued token to still carry review:ionm alongside review:ae");
        Assert.IsTrue(claims.Any(c => c.Type == "consent" && c.Value == "approve"), "expected the issued token to still carry the unrelated consent:approve claim too");
    }

    // ADR-017's own consequence text, verbatim: "a request with a
    // technically-valid bearer token but a missing/invalid DPoP proof is
    // rejected 401." The bearer token here is entirely genuine (a real,
    // just-issued, correctly-scoped token) -- only the DPoP header is absent.
    public static async Task ARequestWithAValidBearerTokenButNoDpopProofIsRejectedWith401(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (token, _) = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); // no DPoP header attached at all
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The actual value ADR-017 buys: a leaked bearer token is useless to an
    // attacker who doesn't also hold the private key it's bound to -- a
    // proof signed by any OTHER key (even a well-formed, correctly-shaped
    // DPoP proof) must still be rejected, since its jkt won't match the
    // token's own cnf.jkt.
    public static async Task ARequestWithADpopProofSignedByADifferentKeyIsRejectedWith401(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (token, _) = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var attackerKey = DpopKeyPair.Generate();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        AttachAuth(request, hostClient, token, attackerKey);
        var response = await hostClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // docs/features/dpop-and-tamper-evidence.md's own "Replaying an
    // already-used DPoP proof (same jti) is rejected" scenario -- a DPoP
    // proof is single-use (RFC 9449); reusing the exact same proof bytes on
    // a second request must be rejected even though every other check
    // (signature, htm/htu, cnf.jkt) still passes.
    public static async Task ReplayingAnAlreadyUsedDpopProofIsRejectedWith401(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (token, key) = await GetTokenAsync(devIdpClient, "publisher-client", "publisher-client-secret", "events:publish");
        var absoluteUri = new Uri(hostClient.BaseAddress!, "/publish/whatever").ToString();
        var proof = key.CreateProof("POST", absoluteUri, token);

        using var first = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        first.Headers.Add("DPoP", proof);
        await hostClient.SendAsync(first); // outcome doesn't matter here -- only that the proof was consumed

        using var replay = new HttpRequestMessage(HttpMethod.Post, "/publish/whatever") { Content = JsonContent(new { }) };
        replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        replay.Headers.Add("DPoP", proof); // the exact same proof bytes, same jti
        var response = await hostClient.SendAsync(replay);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ADR-030 -- "a caller scoped to one AppId cannot resolve or read
    // another's schema." tenant-a-operator-client holds only
    // registry:admin:tenant-a, not the unscoped registry:admin -- it can
    // register/read schemas under "tenant-a", but is Forbidden for any
    // other AppId, even though ScopeAuthorizationHandler's own coarse gate
    // (a registry:admin-shaped scope of SOME kind) lets it reach the
    // endpoint at all.
    public static async Task ATenantScopedTokenCanAdministerItsOwnAppIdButNotAnother(HttpClient hostClient, HttpClient devIdpClient)
    {
        var (tenantToken, tenantKey) = await GetTokenAsync(devIdpClient, "tenant-a-operator-client", "tenant-a-operator-client-secret", "registry:admin:tenant-a");
        const string schema = """{ "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }""";

        using var ownAppIdRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/TenantScopedEvent")
        {
            Content = JsonContent(new
            {
                appId = "tenant-a",
                jsonSchema = schema,
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.Id",
                parentValidationMode = "Permissive",
            }),
        };
        AttachAuth(ownAppIdRequest, hostClient, tenantToken, tenantKey);
        var ownAppIdResponse = await hostClient.SendAsync(ownAppIdRequest);
        Assert.AreEqual(HttpStatusCode.Created, ownAppIdResponse.StatusCode, await ownAppIdResponse.Content.ReadAsStringAsync());

        using var otherAppIdRequest = new HttpRequestMessage(HttpMethod.Put, "/registry/TenantScopedEvent")
        {
            Content = JsonContent(new
            {
                appId = "tenant-b",
                jsonSchema = schema,
                filterableFields = Array.Empty<object>(),
                changeKind = "Full",
                entityIdField = "$.Id",
                parentValidationMode = "Permissive",
            }),
        };
        AttachAuth(otherAppIdRequest, hostClient, tenantToken, tenantKey);
        var otherAppIdResponse = await hostClient.SendAsync(otherAppIdRequest);
        Assert.AreEqual(HttpStatusCode.Forbidden, otherAppIdResponse.StatusCode);

        using var readOtherAppIdRequest = new HttpRequestMessage(HttpMethod.Get, "/registry/TenantScopedEvent?appId=tenant-b");
        AttachAuth(readOtherAppIdRequest, hostClient, tenantToken, tenantKey);
        var readOtherAppIdResponse = await hostClient.SendAsync(readOtherAppIdRequest);
        Assert.AreEqual(HttpStatusCode.Forbidden, readOtherAppIdResponse.StatusCode);
    }

    private static async Task<HttpResponseMessage> Preflight(HttpClient hostClient, string origin, string path = "/publish/whatever")
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await hostClient.SendAsync(request);
    }

    private static HttpContent JsonContent(object value) =>
        System.Net.Http.Json.JsonContent.Create(value);
}
