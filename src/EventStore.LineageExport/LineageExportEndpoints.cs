using EventStore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.LineageExport;

public static class LineageExportEndpoints
{
    public static IServiceCollection AddLineageExport(this IServiceCollection services) => services
        .AddScoped<LineageExportService>()
        .AddScoped<BitemporalPlaybackService>()
        .AddSingleton<LineageExportBundleStore>()
        .AddMemoryCache();

    public static WebApplication MapLineageExportEndpoints(this WebApplication app)
    {
        // ADR-068 -- exportLineage's own produced artifact. The GraphQL
        // resolver already enforced RequiredClaims/masking/audit-logging
        // before this bundleUrl was ever handed out, but retrieval is its
        // own ordinary authenticated request, requiring the SAME
        // "events:lineage:read" scope the export query itself required --
        // matching AttachmentEndpoints' own "an opaque locator is never a
        // substitute for authorization" precedent (ADR-032), not a new,
        // weaker posture invented for this one download.
        app.MapGet("/lineage-exports/{exportId}", (string exportId, LineageExportBundleStore store) =>
        {
            var bundle = store.TryGet(exportId);
            return bundle is null
                ? Results.Problem(
                    type: "https://eventstore.example/problems/not-found",
                    title: "export not found or its retrieval window has expired",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Text(bundle.ToNdjson(), "application/x-ndjson");
        }).RequireAuthorization("events:lineage:read");

        // ADR-068 -- import at a receiving environment. Requires the SAME
        // "events:publish"-shaped authority every other write path in this
        // repo requires, per docs/features/lineage-export-and-playback.md's
        // own "an engineer imports" framing -- this is a real write, not a
        // read, unlike export/playback above.
        app.MapPost("/lineage-imports", async (HttpRequest request, LineageExportService exportService, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var ndjson = await reader.ReadToEndAsync(ct);

            LineageExportBundle bundle;
            try
            {
                bundle = LineageExportBundle.ParseNdjson(ndjson);
            }
            catch (FormatException ex)
            {
                return Results.Problem(
                    type: "https://eventstore.example/problems/malformed-lineage-bundle",
                    title: $"malformed bundle: {ex.Message}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var importedCount = await exportService.ImportAsync(bundle, request.Query["importedFrom"].FirstOrDefault() ?? "unknown", ct);
                return Results.Ok(new { importedCount });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    type: "https://eventstore.example/problems/lineage-import-failed",
                    title: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization("events:publish");

        return app;
    }
}
