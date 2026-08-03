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
        app.MapPut("/registry/{eventType}", async (string eventType, RegisterEventTypeRequest request, SchemaRegistryService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(eventType, request, ct);
            return result switch
            {
                RegisterEventTypeResult.Success success => Results.Created($"/registry/{eventType}/{success.Version}", new { version = success.Version }),
                RegisterEventTypeResult.ValidationFailed failed => Results.BadRequest(new { errors = failed.Errors }),
                _ => Results.Problem(statusCode: 500),
            };
        });

        app.MapGet("/registry/{eventType}", async (string eventType, string appId, SchemaRegistryService service, CancellationToken ct) =>
        {
            var definition = await service.GetActiveAsync(appId, eventType, ct);
            return definition is null ? Results.NotFound() : Results.Text(definition.JsonSchema, "application/json");
        });

        app.MapGet("/registry/{eventType}/{version:int}", async (string eventType, int version, string appId, SchemaRegistryService service, CancellationToken ct) =>
        {
            var definition = await service.GetVersionAsync(appId, eventType, version, ct);
            return definition is null ? Results.NotFound() : Results.Text(definition.JsonSchema, "application/json");
        });

        // Temporary listing surface for this build stage, ADR-012's QUERY method
        // with a body -- superseded by the GraphQL eventTypes(...) resolver once
        // "GraphQL-Only Query Layer" lands (see the correction note on this item
        // in docs/08-build-plan.md).
        app.MapMethods("/registry", ["QUERY"], async (ListEventTypesRequest request, SchemaRegistryService service, CancellationToken ct) =>
        {
            var results = await service.ListAsync(request.AppId, request.Top, request.Skip, ct);
            return Results.Ok(results.Select(e => new { e.Name, e.Version, e.IsActive }));
        });

        return app;
    }
}

public record ListEventTypesRequest(string AppId, int? Top, int? Skip);
