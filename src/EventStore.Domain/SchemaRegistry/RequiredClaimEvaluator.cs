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

    // ADR-043 -- "the check becomes 'does the caller have this claim, AND
    // does it apply to this EntityId' -- not a bare HasClaim boolean." An
    // entity-scope restriction rides alongside the underlying claim as a
    // SEPARATE, companion claim (type "{requiredClaim}:entityScope", one
    // value per EntityId the holder's grant is restricted to) rather than
    // encoding it into the claim's own value -- this keeps HasClaim/HasAny
    // above completely unaware entity scoping exists at all, unaffected for
    // every caller that never has a concrete EntityId to check against. No
    // companion claim present at all means unscoped -- ADR-043's own
    // "unaffected, default case," applies wherever the claim ordinarily would.
    public static bool HasClaimForEntity(ClaimsPrincipal user, string requiredClaim, string? entityId)
    {
        if (!HasClaim(user, requiredClaim))
            return false;

        var scopeClaimType = $"{requiredClaim}:entityScope";
        var scopeValues = user.FindAll(scopeClaimType).Select(c => c.Value).ToList();
        if (scopeValues.Count == 0)
            return true;

        return entityId is not null && scopeValues.Contains(entityId);
    }
}
