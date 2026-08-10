using System.Security.Claims;

namespace EventStore.IntegrationTests;

// A no-claims principal for every existing scenario that registers event
// types with no RequiredClaims -- RequiredClaimEvaluator.HasAny() returns
// true unconditionally when a direction's list is empty, so this
// satisfies every pre-item-7 test unchanged. AuthScenarioAssertions
// exercises real claim-bearing principals (via real issued tokens)
// separately.
internal static class TestClaimsPrincipal
{
    public static readonly ClaimsPrincipal None = new(new ClaimsIdentity());

    // "type:value" format, matching RequiredClaim.Claim/RequiredClaimEvaluator.
    public static ClaimsPrincipal With(string typeValueClaim)
    {
        var separatorIndex = typeValueClaim.IndexOf(':');
        var identity = new ClaimsIdentity([new Claim(typeValueClaim[..separatorIndex], typeValueClaim[(separatorIndex + 1)..])]);
        return new ClaimsPrincipal(identity);
    }

    // Arbitrary named claims -- for scenarios naming real OIDC claim types
    // directly (e.g. "acr"/"auth_time"/"sub", ADR-066) rather than this
    // project's own "type:value" RequiredClaim format above.
    public static ClaimsPrincipal WithClaims(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));
}
