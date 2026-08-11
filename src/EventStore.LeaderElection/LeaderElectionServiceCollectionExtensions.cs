using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStore.LeaderElection;

public static class LeaderElectionServiceCollectionExtensions
{
    // TryAddScoped, not AddScoped -- ADR-078's own mechanism is shared by
    // every worker role that needs it (Router, the peer-sync outbox pump,
    // and eventually the webhook outbox pump), so each of THEIR own
    // Add{Role}() extensions calls this too; only the first registration
    // for a given Host process should actually take effect.
    public static IServiceCollection AddLeaderElection(this IServiceCollection services)
    {
        services.TryAddScoped<LeaderElectionService>();
        return services;
    }
}
