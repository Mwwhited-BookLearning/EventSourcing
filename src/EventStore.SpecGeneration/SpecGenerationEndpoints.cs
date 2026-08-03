using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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

    public static WebApplication MapSpecGenerationEndpoints(this WebApplication app)
    {
        app.MapGet("/openapi.json", async (OpenApiDocumentBuilder builder, CancellationToken ct) =>
            Results.Text(await builder.GetOrBuildJsonAsync(ct), "application/json")).AllowAnonymous();

        app.MapGet("/asyncapi.json", async (AsyncApiDocumentBuilder builder, CancellationToken ct) =>
            Results.Text(await builder.GetOrBuildJsonAsync(ct), "application/json")).AllowAnonymous();

        return app;
    }
}
