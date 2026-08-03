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

        if (scopes.Contains(requirement.RequiredScope))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
