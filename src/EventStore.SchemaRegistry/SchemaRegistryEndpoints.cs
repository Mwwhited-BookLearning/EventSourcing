using EventStore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.SchemaRegistry;

public static class SchemaRegistryEndpoints
{
    public static IServiceCollection AddSchemaRegistry(this IServiceCollection services) =>
        services.AddScoped<SchemaRegistryService>();

    public static WebApplication MapSchemaRegistryEndpoints(this WebApplication app)
    {
        // Every registry endpoint requires registry:admin -- registration is
        // control-plane configuration, the same scope tier throughout
        // (docs/05-schema-registry-and-spec-generation.md, ADR-006).
        var registry = app.MapGroup("/registry").RequireAuthorization("registry:admin");

        registry.MapPut("/{eventType}", async (string eventType, RegisterEventTypeRequest request, SchemaRegistryService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(eventType, request, ct);
            return result switch
            {
                RegisterEventTypeResult.Success success => Results.Created($"/registry/{eventType}/{success.Version}", new { version = success.Version }),
                RegisterEventTypeResult.ValidationFailed failed => Results.BadRequest(new { errors = failed.Errors }),
                _ => Results.Problem(statusCode: 500),
            };
        });

        registry.MapGet("/{eventType}", async (string eventType, string appId, SchemaRegistryService service, CancellationToken ct) =>
        {
            var definition = await service.GetActiveAsync(appId, eventType, ct);
            return definition is null ? Results.NotFound() : Results.Text(definition.JsonSchema, "application/json");
        });

        registry.MapGet("/{eventType}/{version:int}", async (string eventType, int version, string appId, SchemaRegistryService service, CancellationToken ct) =>
        {
            var definition = await service.GetVersionAsync(appId, eventType, version, ct);
            return definition is null ? Results.NotFound() : Results.Text(definition.JsonSchema, "application/json");
        });

        // "CQRS Read-Model Projections" -- ProjectionHost's only reachable
        // dependency on the write side is HTTP (docs/06-solution-structure.md),
        // so ChangeKind (needed to pick Full-replace vs. Partial-merge) must be
        // exposed this way rather than a direct service reference. Deliberately
        // NOT on the registry:admin-gated group above: this is metadata any
        // Follow consumer needs (the same tier as the schema itself), not a
        // control-plane action -- gated by events:follow, the scope a
        // projections client already holds, instead.
        app.MapGet("/registry/{eventType}/change-kind", async (string eventType, SchemaRegistryService service, CancellationToken ct) =>
        {
            var changeKind = await service.GetActiveChangeKindByNameAsync(eventType, ct);
            return changeKind is null ? Results.NotFound() : Results.Ok(new { changeKind = changeKind.ToString() });
        }).RequireAuthorization("events:follow");

        // Temporary listing surface for this build stage, ADR-012's QUERY method
        // with a body -- superseded by the GraphQL eventTypes(...) resolver once
        // "GraphQL-Only Query Layer" lands (see the correction note on this item
        // in docs/08-build-plan.md).
        registry.MapMethods("/", ["QUERY"], async (ListEventTypesRequest request, SchemaRegistryService service, CancellationToken ct) =>
        {
            var results = await service.ListAsync(request.AppId, request.Top, request.Skip, ct);
            return Results.Ok(results.Select(e => new { e.Name, e.Version, e.IsActive }));
        });

        return app;
    }
}

public record ListEventTypesRequest(string AppId, int? Top, int? Skip);
