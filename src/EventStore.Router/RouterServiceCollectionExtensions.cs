using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Router;

public static class RouterServiceCollectionExtensions
{
    public static IServiceCollection AddRouter(this IServiceCollection services) =>
        services.AddHostedService<RouterWorker>();
}
