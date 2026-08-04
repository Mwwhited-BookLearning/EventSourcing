using System.Security.Claims;
using EventStore.Domain.SchemaRegistry;
using EventStore.SchemaRegistry;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "Registry listing -- GraphQL query field
// (ADR-037)": the pre-ADR-037 QUERY /registry surface (SchemaRegistryEndpoints,
// "Temporary listing surface... superseded by the GraphQL eventTypes(...)
// resolver") re-expressed as static, hand-written GraphQL types -- unlike
// Follow, this surface has no per-registered-type dynamic shape at all
// (eventTypes/eventType return a FIXED shape regardless of which event type
// is named), so it needs none of EntityTypeSubscriptionTypeModule's dynamic
// type generation.
[ExtendObjectType(OperationTypeNames.Query)]
public class RegistryQueries
{
    [GraphQLName("eventTypes")]
    public async Task<IReadOnlyList<EventTypeDefinition>> GetEventTypesAsync(
        string appId, int? first, int? skip, [Service] ClaimsPrincipal user, SchemaRegistryService registry, IAuthorizationService authorizationService, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "registry:admin");
        if (!AppIdScopeEvaluator.CanAdminister(user, appId))
            throw new GraphQLException("Forbidden -- caller's registry:admin scope does not cover this appId.");

        return await registry.ListAsync(appId, first, skip, ct);
    }

    [GraphQLName("eventType")]
    public async Task<EventTypeDefinition?> GetEventTypeAsync(
        string appId, string name, int version, [Service] ClaimsPrincipal user, SchemaRegistryService registry, IAuthorizationService authorizationService, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "registry:admin");
        if (!AppIdScopeEvaluator.CanAdminister(user, appId))
            throw new GraphQLException("Forbidden -- caller's registry:admin scope does not cover this appId.");

        return await registry.GetVersionAsync(appId, name, version, ct);
    }
}
