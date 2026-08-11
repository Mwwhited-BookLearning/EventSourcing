using System.Security.Claims;
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
        MapLineageQuery(app, "parents", (svc, id, user, top, skip, ct) => svc.GetParentsAsync(id, user, top, skip, ct));
        MapLineageQuery(app, "children", (svc, id, user, top, skip, ct) => svc.GetChildrenAsync(id, user, top, skip, ct));
        MapLineageQuery(app, "ancestors", (svc, id, user, top, skip, ct) => svc.GetAncestorsAsync(id, user, top, skip, ct));
        MapLineageQuery(app, "descendants", (svc, id, user, top, skip, ct) => svc.GetDescendantsAsync(id, user, top, skip, ct));
        return app;
    }

    private static void MapLineageQuery(
        WebApplication app,
        string relation,
        Func<LineageService, Guid, ClaimsPrincipal, int?, int?, CancellationToken, Task<IReadOnlyList<LineageNode>>> resolve)
    {
        app.MapMethods($"/events/{{id}}/{relation}", ["QUERY"], async (Guid id, LineageQueryRequest? request, ClaimsPrincipal user, LineageService service, CancellationToken ct) =>
        {
            var rootCheck = await service.CheckRootAsync(id, user, ct);
            switch (rootCheck)
            {
                case LineageRootCheck.NotFound:
                    return Results.NotFound(new { error = $"event {id} not found" });
                case LineageRootCheck.Forbidden:
                    return Results.Forbid();
            }

            var nodes = await resolve(service, id, user, request?.Top, request?.Skip, ct);
            return Results.Ok(nodes.Select(n => new { n.EventId, n.EventType, n.SequenceNumber, n.OccurredAt, n.Resolved, n.Restricted }));
        }).RequireAuthorization("events:lineage:read");
    }
}
