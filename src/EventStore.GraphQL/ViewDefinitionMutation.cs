using System.Security.Claims;
using EventStore.ViewRegistry;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// "MVVM Client" (ADR-039) -- registering a new ViewDefinition version is
// registry administration, the same registry:admin scope RegistryQueries'
// eventTypes/eventType already gate (no AppId to check against
// AppIdScopeEvaluator -- ViewDefinition has none, docs/data/schema-
// registry.md's own key).
[ExtendObjectType(OperationTypeNames.Mutation)]
public class ViewDefinitionMutation
{
    [GraphQLName("registerViewDefinition")]
    public async Task<RegisterViewDefinitionPayload> RegisterViewDefinitionAsync(
        string entityType, string viewKind, List<int> compatibleSchemaVersions, string templateContent,
        [Service] ClaimsPrincipal user, ViewDefinitionService viewRegistry, IAuthorizationService authorizationService, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "registry:admin");

        var result = await viewRegistry.RegisterAsync(
            new RegisterViewDefinitionRequest(entityType, viewKind, compatibleSchemaVersions, templateContent), ct);

        return result switch
        {
            RegisterViewDefinitionResult.Success success => new RegisterViewDefinitionPayload(success.Version, success.Hash, null),
            RegisterViewDefinitionResult.ValidationFailed failed => throw new GraphQLException(string.Join("; ", failed.Errors)),
            _ => throw new GraphQLException("Unexpected registration outcome."),
        };
    }
}

public record RegisterViewDefinitionPayload(int Version, string Hash, string? Reason);
