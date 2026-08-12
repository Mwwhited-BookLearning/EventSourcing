using System.Security.Claims;

namespace EventStore.Domain.SchemaRegistry;

// ADR-066/RFC 9470 -- the pure step-up-authentication check, shared by every
// call site that gates an action on RequiredSignature-shaped configuration.
// Originally private to EventStore.Inbox.PublishService (publish-time
// enforcement); extracted here, unchanged, so EventStore.GraphQL's
// RevealFieldMutation can apply the identical check to a per-field
// x-masking.requiredSignature configuration without duplicating it or
// needing a new cross-project reference (EventStore.Domain is already
// transitively available to both).
public static class StepUpEvaluator
{
    // JwtBearer's own default MapInboundClaims=true remaps the token's "acr"
    // claim to this long-form URI before any resolver ever sees it --
    // confirmed against JwtSecurityTokenHandler.DefaultInboundClaimTypeMap,
    // the exact same class of remapping AccessLogReaderContext.Resolve's own
    // comment already documents for "sub"/ClaimTypes.NameIdentifier -- both
    // checked so this works the same whether or not that remapping ran.
    public static string? ResolveAcr(ClaimsPrincipal user) =>
        user.FindFirst("http://schemas.microsoft.com/claims/authnclassreference")?.Value ?? user.FindFirst("acr")?.Value;

    // Both checks are independent and additive: an AcrValues list with no
    // matching acr claim fails regardless of MaxAge, and a MaxAge with no
    // (or too-old) auth_time fails regardless of acr. Either half being
    // unconfigured (empty AcrValues, null MaxAge) is simply never checked --
    // a RequiredSignature naming only one of the two is exactly as valid as
    // naming both.
    public static bool IsSatisfied(ClaimsPrincipal user, RequiredSignature requiredSignature, string? acr)
    {
        if (requiredSignature.AcrValues.Count > 0 && (acr is null || !requiredSignature.AcrValues.Contains(acr)))
            return false;

        if (requiredSignature.MaxAge is { } maxAgeSeconds)
        {
            var authTimeClaim = user.FindFirst("auth_time")?.Value;
            if (!long.TryParse(authTimeClaim, out var authTimeUnixSeconds))
                return false;
            var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authTimeUnixSeconds);
            if (DateTimeOffset.UtcNow - authenticatedAt > TimeSpan.FromSeconds(maxAgeSeconds))
                return false;
        }

        return true;
    }
}
