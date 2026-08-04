using EventStore.Projections.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Projections.Host;

public static class ProjectionHostServiceCollectionExtensions
{
    public static IServiceCollection AddProjection<TReadModel, TProjection>(this IServiceCollection services)
        where TReadModel : class
        where TProjection : class, IProjection<TReadModel>
    {
        services.AddSingleton<IProjection<TReadModel>, TProjection>();
        services.AddHostedService<ProjectionHost<TReadModel>>();
        return services;
    }
}
