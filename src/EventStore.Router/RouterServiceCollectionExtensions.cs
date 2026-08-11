using EventStore.LeaderElection;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Router;

public static class RouterServiceCollectionExtensions
{
    public static IServiceCollection AddRouter(this IServiceCollection services) => services
        .AddLeaderElection()
        .AddHostedService<RouterWorker>();
}
