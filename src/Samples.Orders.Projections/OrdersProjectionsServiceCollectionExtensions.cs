using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Samples.Orders.Projections;

public static class OrdersProjectionsServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersProjections(this IServiceCollection services, string sqliteConnectionString)
    {
        services.AddDbContext<OrdersProjectionsDbContext>(o => o.UseSqlite(sqliteConnectionString));
        // ProjectionHost<T> resolves the abstract ProjectionsDbContext, not the
        // concrete type AddDbContext registered -- this is what makes that resolve.
        services.AddScoped<ProjectionsDbContext>(sp => sp.GetRequiredService<OrdersProjectionsDbContext>());
        services.AddProjection<OrderSummary, OrderSummaryProjection>();
        return services;
    }
}
