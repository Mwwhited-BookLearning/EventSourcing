using System.Security.Claims;

namespace EventStore.Domain.SchemaRegistry;

// docs/06-solution-structure.md's HasRequiredClaim/HasAnyRequiredClaim sketch,
// shared here since Publish (EventStore.Inbox), Follow (EventStore.Follow.Api),
// and Lineage (EventStore.Lineage.Api) all need the identical OR-matched,
// per-Direction check against ADR-008/050's already-built RequiredClaims list.
public static class RequiredClaimEvaluator
{
    public static bool HasAny(IReadOnlyList<RequiredClaim> requiredClaims, ClaimDirection direction, ClaimsPrincipal user)
    {
        var forDirection = requiredClaims.Where(c => c.Direction == direction).ToList();
        return forDirection.Count == 0 || forDirection.Any(c => HasClaim(user, c.Claim));
    }

    // Public: ADR-009 deliberately reuses this exact "type:value" primitive at
    // the property level (x-masking.requiredClaim), not a second parser --
    // PayloadMasker's caller-supplied hasClaim delegate calls this directly.
    public static bool HasClaim(ClaimsPrincipal user, string requiredClaim)
    {
        var separatorIndex = requiredClaim.IndexOf(':');
        var type = requiredClaim[..separatorIndex];
        var value = requiredClaim[(separatorIndex + 1)..];
        return user.HasClaim(type, value);
    }
}
