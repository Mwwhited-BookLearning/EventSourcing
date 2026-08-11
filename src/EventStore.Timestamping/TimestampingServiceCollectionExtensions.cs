using EventStore.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Timestamping;

public static class TimestampingServiceCollectionExtensions
{
    // Only registered when Timestamping:TsaUrl is actually configured --
    // the same "no silent fallback" posture ErasureServiceCollectionExtensions
    // already established for HashiCorpVault. An event type opting into
    // EnableRfc3161Timestamp with no TSA configured gets a clear DI
    // resolution failure at first publish, not a silently-skipped timestamp.
    public static IServiceCollection AddTimestamping(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TimestampingOptions>(o => configuration.GetSection("Timestamping").Bind(o));

        var tsaUrl = configuration["Timestamping:TsaUrl"];
        if (tsaUrl is not null)
        {
            services.AddHttpClient<ITimestampAuthorityClient, HttpTimestampAuthorityClient>();
        }

        return services;
    }
}
