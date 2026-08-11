using EventStore.Inbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStore.Archival;

public static class ArchivalServiceCollectionExtensions
{
    public static IServiceCollection AddArchival(this IServiceCollection services)
    {
        // TryAdd, not Add -- EventStore.Inbox/PublishEndpoints.cs already
        // registers both verifiers for /events/verify and /access-log/verify;
        // only the first registration for a given Host process should
        // actually take effect, the same LeaderElectionServiceCollectionExtensions
        // convention this mirrors.
        services.TryAddScoped<ChainVerificationService>();
        services.TryAddScoped<AccessLogChainVerificationService>();
        services.AddScoped<ArchivalService>();
        return services;
    }
}
