using System.Security.Claims;
using System.Text.Json;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.FeatureFlags;

public static class FeatureFlagEndpoints
{
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services) =>
        services.AddScoped<FeatureFlagService>();

    // ADR-077 -- reuses registry:admin, the same "admin tier" scope
    // SchemaRegistryEndpoints/RbacEndpoints already gate their own
    // control-plane writes with, rather than inventing a dedicated
    // feature-flags scope for a mechanism this narrow.
    public static WebApplication MapFeatureFlagEndpoints(this WebApplication app)
    {
        app.MapPut("/feature-flags/{key}", async (string key, SetFeatureFlagRequest request, ClaimsPrincipal user, FeatureFlagService featureFlags, CancellationToken ct) =>
        {
            if (!AppIdScopeEvaluator.CanAdminister(user, request.AppId))
                return Results.Forbid();

            var value = request.Value.GetRawText();
            var result = await featureFlags.SetFlagAsync(request.AppId, key, value, user, ct);
            return result switch
            {
                PublishResult.Accepted a => Results.Ok(new { sequenceNumber = a.SequenceNumber }),
                PublishResult.UnregisteredEventType => Results.Problem(statusCode: 500, detail: "the reserved event type was not registered before publishing -- this is an EnsureRegisteredAsync bug, not a caller error"),
                PublishResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("registry:admin");

        return app;
    }
}

// Value is an arbitrary JSON shape (a flag isn't always boolean, per
// docs/data/schema-registry.md's own FeatureFlagState comment) -- captured
// as JsonElement and re-serialized to its compact text form for storage,
// rather than constraining callers to one .NET type.
public record SetFeatureFlagRequest(string AppId, JsonElement Value);
