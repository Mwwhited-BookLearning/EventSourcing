using EventStore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Lineage.Api;

public static class LineageEndpoints
{
    public static IServiceCollection AddLineageApi(this IServiceCollection services) =>
        services.AddScoped<LineageService>();

    public static WebApplication MapLineageEndpoints(this WebApplication app)
    {
        MapLineageQuery(app, "parents", (svc, id, top, skip, ct) => svc.GetParentsAsync(id, top, skip, ct));
        MapLineageQuery(app, "children", (svc, id, top, skip, ct) => svc.GetChildrenAsync(id, top, skip, ct));
        MapLineageQuery(app, "ancestors", (svc, id, top, skip, ct) => svc.GetAncestorsAsync(id, top, skip, ct));
        MapLineageQuery(app, "descendants", (svc, id, top, skip, ct) => svc.GetDescendantsAsync(id, top, skip, ct));
        return app;
    }

    private static void MapLineageQuery(
        WebApplication app,
        string relation,
        Func<LineageService, Guid, int?, int?, CancellationToken, Task<IReadOnlyList<LineageNode>>> resolve)
    {
        app.MapMethods($"/events/{{id}}/{relation}", ["QUERY"], async (Guid id, LineageQueryRequest? request, LineageService service, CancellationToken ct) =>
        {
            if (!await service.EventExistsAsync(id, ct))
                return Results.NotFound(new { error = $"event {id} not found" });

            var nodes = await resolve(service, id, request?.Top, request?.Skip, ct);
            return Results.Ok(nodes.Select(n => new { n.EventId, n.EventType, n.SequenceNumber, n.OccurredAt, n.Resolved }));
        });
    }
}
