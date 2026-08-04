using System.Security.Claims;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md's scope table applies identically to GraphQL,
// "enforced per-field/per-operation via HotChocolate's own authorization
// directives, checked against the same bearer token -- not a second auth
// stack." This item's dynamically-built (FollowSubscriptionTypeModule) and
// hand-written (RegistryQueries/LineageQueries) resolvers check per-field
// scopes explicitly rather than via HotChocolate's [Authorize] attribute
// (which needs a compile-time-known policy per field, awkward for a
// dynamically-built field) -- reusing the SAME registered ASP.NET Core
// policies (HostCoreExtensions) through the ordinary IAuthorizationService,
// not a second scope-matching implementation.
public static class GraphQlAuth
{
    public static async Task RequireScopeAsync(IAuthorizationService authorizationService, ClaimsPrincipal user, string policy)
    {
        var result = await authorizationService.AuthorizeAsync(user, policy);
        if (!result.Succeeded)
            throw new GraphQLException($"Forbidden -- caller's token does not hold the required \"{policy}\" scope.");
    }
}
