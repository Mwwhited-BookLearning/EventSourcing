using EventStore.LeaderElection;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Webhooks;

public static class WebhookServiceCollectionExtensions
{
    // A plain named HttpClient -- no DPoP/OAuth of its own (unlike
    // PeerSyncClient/FollowClient's own site-to-site or client-to-server
    // calls): the target is an arbitrary external HTTPS endpoint this
    // framework has no credential for, and the Standard Webhooks signature
    // itself is what lets that target trust the request (ADR-060).
    public static IServiceCollection AddWebhooks(this IServiceCollection services)
    {
        services.AddLeaderElection();
        services.AddHttpClient("Webhooks");
        services.AddSingleton<WebhookRetryTracker>();
        services.AddScoped<WebhookSubscriptionService>();
        services.AddHostedService<WebhookOutboxPump>();
        return services;
    }
}
