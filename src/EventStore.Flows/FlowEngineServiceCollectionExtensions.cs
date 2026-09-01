using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.Flows;

public static class FlowEngineServiceCollectionExtensions
{
    // Mirrors AddOrdersProjections' own shape exactly (Samples.Orders.Projections),
    // one PendingTasksDbContext shared by every registered flow.
    public static IServiceCollection AddFlowEngine(this IServiceCollection services, string sqliteConnectionString)
    {
        services.AddDbContext<PendingTasksDbContext>(o => o.UseSqlite(sqliteConnectionString));
        // ProjectionHost<T> resolves the abstract ProjectionsDbContext, not the
        // concrete type AddDbContext registered -- this is what makes that resolve.
        services.AddScoped<ProjectionsDbContext>(sp => sp.GetRequiredService<PendingTasksDbContext>());
        return services;
    }

    // Deliberately NOT AddProjection<TReadModel,TProjection> (EventStore.
    // Projections.Host's own helper): that helper does one AddSingleton<
    // IProjection<TReadModel>,TProjection>() + one AddHostedService<
    // ProjectionHost<TReadModel>>() per TReadModel type -- correct for
    // exactly one projection per read-model type (OrderSummaryProjection's
    // own usage), but every flow here shares the SAME PendingTask type, so
    // that pattern would silently let only the last-registered flow's
    // IProjection<PendingTask> ever resolve. Each flow gets its own
    // ProjectionHost<PendingTask> instance instead, built via
    // ActivatorUtilities so the other constructor parameters (scope
    // factory, FollowClient, options, logger) still resolve from the
    // container exactly as AddProjection's own registration would.
    public static IServiceCollection AddFlow(this IServiceCollection services, FlowDefinition flow)
    {
        services.AddSingleton<IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<ProjectionHost<PendingTask>>(sp, new FlowProjection(flow)));
        return services;
    }
}
