using System.Security.Claims;
using EventStore.SchemaRegistry;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

public record SchemaCapabilities(int ActiveVersion, IReadOnlyList<int> SupportedWindow);

// docs/features/compatibility-and-versioning.md's "version-discovery
// capability negotiation" diagram, realized as a dedicated Query field
// rather than as an argument threaded through the dynamic Subscription
// field FollowSubscriptionTypeModule already builds -- that diagram is
// itself flagged there as "this doc's own structural choice, not a shape
// ADR-038 states," so a small, self-contained field keeps this mechanism
// independent of that already-intricate, hot-reload-limited surface.
//
// supportedWindow is a fixed numeric [active-1, active, active+1] band
// (values below 1 dropped) -- not filtered against which versions actually
// have a registered row, because this design's own registration model has
// no "registered but not yet active" state for a future version to occupy
// (SchemaRegistryService.RegisterAsync always activates the version it just
// created and deactivates the prior one in the same transaction) -- a
// named, honest narrowing of that diagram's own "version 4 not yet active"
// framing, which this repo's actual mechanics can't literally produce.
[ExtendObjectType(OperationTypeNames.Query)]
public class CapabilitiesQueries
{
    [GraphQLName("capabilities")]
    public async Task<SchemaCapabilities> GetCapabilitiesAsync(
        string appId, string name, IReadOnlyList<int> supportedSchemaVersions,
        [Service] ClaimsPrincipal user, SchemaRegistryService registry, IAuthorizationService authorizationService, CancellationToken ct)
    {
        // Gated the same way Follow's own connect-time check is
        // (events:follow) -- this is the connection-open step for a Follow-
        // style client, not a registry-administration action.
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:follow");

        var activeDefinition = await registry.GetActiveAsync(appId, name, ct)
            ?? throw new GraphQLException("This event type is not registered under this appId.");

        var window = new[] { activeDefinition.Version - 1, activeDefinition.Version, activeDefinition.Version + 1 }
            .Where(v => v >= 1)
            .ToList();

        if (!supportedSchemaVersions.Intersect(window).Any())
            throw new GraphQLException("Capability mismatch -- no schema version this client understands is still served.");

        return new SchemaCapabilities(activeDefinition.Version, window);
    }
}
