using EventStore.LeaderElection;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.ExpectedResponse;

public static class ExpectedResponseServiceCollectionExtensions
{
    public static IServiceCollection AddExpectedResponseTracking(this IServiceCollection services) => services
        .AddLeaderElection()
        .AddHostedService<ExpectedResponseWatcher>();
}
