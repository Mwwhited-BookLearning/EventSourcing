using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.SpecGeneration;

public static class SpecGenerationEndpoints
{
    public static IServiceCollection AddSpecGeneration(this IServiceCollection services) => services
        .AddMemoryCache()
        .AddSingleton<EventSchemaConverter>()
        .AddSingleton<MaskingSchemaTransformer>()
        .AddScoped<OpenApiDocumentBuilder>()
        .AddScoped<AsyncApiDocumentBuilder>();

    // ADR-002 -- "a single config flag (SpecEndpoints:Enabled) turns the
    // routes off completely -- not just hiding the UI while leaving the
    // raw JSON reachable, the actual MapGet registrations are conditional
    // on it." Defaults to true (every pre-existing deployment/test with no
    // such config section is unaffected) -- a deployment with the
    // stricter posture that ADR names sets it to false explicitly.
    public static WebApplication MapSpecGenerationEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue("SpecEndpoints:Enabled", true))
            return app;

        app.MapGet("/openapi.json", async (OpenApiDocumentBuilder builder, CancellationToken ct) =>
            Results.Text(await builder.GetOrBuildJsonAsync(ct), "application/json")).AllowAnonymous();

        app.MapGet("/asyncapi.json", async (AsyncApiDocumentBuilder builder, CancellationToken ct) =>
            Results.Text(await builder.GetOrBuildJsonAsync(ct), "application/json")).AllowAnonymous();

        return app;
    }
}
