using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.TicketExchange;

// ADR-040's "resolution" hop -- a second authentication scheme, additive
// to JwtBearer (the default), never replacing it. Only ever activated on
// the specific routes that opt in (Streaming playback, Attachment
// retrieval) via `AuthorizeAttribute.AuthenticationSchemes`; every other
// endpoint's Bearer-only authentication is completely unaffected. Returns
// NoResult (not Fail) when no `ticket` query parameter is present at all,
// so a route accepting both schemes falls through to Bearer/JwtBearer
// cleanly instead of this handler always winning or always failing.
public class TicketAuthenticationHandler(
    IOptionsMonitor<TicketAuthenticationOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, IHttpClientFactory httpClientFactory)
    : AuthenticationHandler<TicketAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ticket = Request.Query["ticket"].ToString();
        var sig = Request.Query["sig"].ToString();
        if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(sig))
            return AuthenticateResult.NoResult();

        var client = httpClientFactory.CreateClient();
        using var response = await client.PostAsync(
            Options.IntrospectionEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = ticket,
                ["token_type_hint"] = "urn:eventstore:token-type:ticket",
                ["sig"] = sig,
            }));

        if (!response.IsSuccessStatusCode)
            return AuthenticateResult.Fail("Ticket introspection request failed.");

        var body = await response.Content.ReadFromJsonAsync<IntrospectionResponse>();
        if (body is null || !body.Active)
            return AuthenticateResult.Fail("Ticket is unknown, expired, already consumed, or its signature does not match.");

        // EventStore.DevIdp already excludes registration-time/possession-
        // proof claims (TicketClaims.ExcludedClaimTypes) before ever storing
        // them on the Ticket record -- nothing here needs to filter again.
        var identity = new ClaimsIdentity(authenticationType: TicketAuthenticationDefaults.AuthenticationScheme);
        foreach (var claim in body.Claims ?? [])
            identity.AddClaim(new Claim(claim.Type, claim.Value));

        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, TicketAuthenticationDefaults.AuthenticationScheme));
    }

    private sealed record IntrospectionResponse(bool Active, List<ClaimEntry>? Claims);

    private sealed record ClaimEntry(string Type, string Value);
}
