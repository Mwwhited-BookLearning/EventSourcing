using System.Security.Claims;
using EventStore.Domain.Views;
using EventStore.ViewRegistry;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// "MVVM Client" (ADR-039) -- the client-side ViewModel's own lookup step
// (docs/features/mvvm-client.md's "rendering a ViewDefinition, with generic
// fallback" sequence diagram): `null` is the signal to render the generic
// property-list fallback rather than an error, matching that diagram's own
// "not found" branch exactly. Gated by events:follow, not registry:admin --
// this is an ordinary client read, not registry administration.
[ExtendObjectType(OperationTypeNames.Query)]
public class ViewDefinitionQueries
{
    [GraphQLName("viewDefinition")]
    public async Task<ViewDefinition?> GetViewDefinitionAsync(
        string entityType, string? viewKind, int? schemaVersion,
        [Service] ClaimsPrincipal user, ViewDefinitionService viewRegistry, IAuthorizationService authorizationService, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:follow");
        return await viewRegistry.GetActiveAsync(entityType, viewKind ?? "Detail", schemaVersion, ct);
    }
}
