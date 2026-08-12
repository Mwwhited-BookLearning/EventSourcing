using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

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

        // ADR-025 -- a pure presentation layer on top of /openapi.json above,
        // never a second generation path: OpenApiRoutePattern points Scalar
        // at the SAME on-demand document ADR-002's own OpenApiDocumentBuilder
        // produces, rather than the built-in Microsoft.AspNetCore.OpenApi
        // convention (/openapi/{documentName}.json) this project doesn't use.
        app.MapScalarApiReference(options => options.WithOpenApiRoutePattern("/openapi.json")).AllowAnonymous();

        // ADR-025 -- AsyncAPI has no .NET-native renderer to lean on (unlike
        // Scalar for OpenAPI); a single static HTML page loading
        // @asyncapi/react-component from a CDN, pointed at the existing
        // /asyncapi.json endpoint, is the same "single HTML file" simplicity
        // Scalar itself uses for non-.NET stacks. Read-only Studio is
        // deliberately NOT used -- this route browses generated output, it
        // never hand-authors a spec.
        app.MapGet("/asyncapi-ui", () => Results.Content(AsyncApiUiPage.Html, "text/html")).AllowAnonymous();

        return app;
    }
}
