using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Upcasting;

public static class UpcastingServiceCollectionExtensions
{
    // ADR-053 -- "swappable per deployment via configuration," resolved
    // through the explicit composition root, no reflection-based auto-
    // selection (ADR-041). "One engine active per deployment, not mixed
    // per event type" (ADR-053's own Decision) is exactly why this is a
    // plain if/else picking ONE implementation to register, not a keyed-
    // service registration the way ADR-057's multiple simultaneously-
    // available erasure key store backends need.
    public static IServiceCollection AddUpcasting(this IServiceCollection services, IConfiguration configuration)
    {
        var engine = configuration["Upcasting:Engine"];
        if (string.Equals(engine, "Jsonata", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IUpcastExpressionEvaluator, JsonataUpcastExpressionEvaluator>();
        else
            services.AddSingleton<IUpcastExpressionEvaluator, CelUpcastExpressionEvaluator>();

        return services
            .AddSingleton<UpcastChain>()
            .AddSingleton<DowncastChain>();
    }
}
