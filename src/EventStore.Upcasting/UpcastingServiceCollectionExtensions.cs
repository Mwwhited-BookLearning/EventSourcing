using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Upcasting;

public static class UpcastingServiceCollectionExtensions
{
    public static IServiceCollection AddUpcasting(this IServiceCollection services) => services
        .AddSingleton<IUpcastExpressionEvaluator, CelUpcastExpressionEvaluator>()
        .AddSingleton<UpcastChain>();
}
