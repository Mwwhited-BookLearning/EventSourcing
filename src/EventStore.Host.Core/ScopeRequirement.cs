using Microsoft.AspNetCore.Authorization;

namespace EventStore.Host.Core;

// OAuth2 delivers `scope` as one space-delimited claim value
// ("events:publish events:follow"), not a repeated claim -- the built-in
// RequireClaim does an exact-value match against a single claim value and
// would reject a token whose scope claim also carries other scopes.
// ADR-006.
public sealed class ScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var scopeClaim = context.User.FindFirst("scope")?.Value;
        var scopes = scopeClaim?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

        // ADR-030 -- an operation-level scope like "registry:admin" now has
        // optional AppId-scoped variants ("registry:admin:{appId}"). Holding
        // only the scoped form still passes this coarse gate (the caller may
        // call the endpoint at all); it's SchemaRegistryEndpoints' own
        // AppIdScopeEvaluator check that decides whether the specific AppId
        // being acted on is actually the one the token is scoped to.
        if (scopes.Contains(requirement.RequiredScope) ||
            scopes.Any(s => s.StartsWith(requirement.RequiredScope + ":", StringComparison.Ordinal)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
