using System.Security.Claims;

namespace EventStore.SchemaRegistry;

// ADR-030 -- the fine-grained half of the registry:admin check.
// ScopeAuthorizationHandler's coarse gate (EventStore.Host.Core) only
// confirms the caller holds SOME registry:admin-shaped scope, unscoped or
// AppId-scoped; this decides whether that specific caller may act on this
// specific AppId. The unscoped form always passes (framework-operator,
// works across every AppId, per ADR-030's "nothing already issued or
// seeded breaks" text); the scoped form passes only for its own AppId.
public static class AppIdScopeEvaluator
{
    public static bool CanAdminister(ClaimsPrincipal user, string appId)
    {
        var scopeClaim = user.FindFirst("scope")?.Value;
        var scopes = scopeClaim?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return scopes.Contains("registry:admin") || scopes.Contains($"registry:admin:{appId}");
    }
}
