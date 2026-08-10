using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Gateway;

// ADR-058 -- three named policies, one per resource shape, attached per
// YARP route via RouteConfig.RateLimiterPolicy (appsettings.json's own
// ReverseProxy:Routes:*:RateLimiterPolicy) rather than one global limiter --
// Token Bucket for publish traffic, Concurrency for Follow's own long-lived
// connections, Sliding Window as the general-purpose default for
// everything else (ordinary GraphQL queries, registry/RBAC/feature-flag
// admin calls). app.UseRateLimiter() runs before app.MapReverseProxy() in
// Program.cs, so a rejected request is answered here and never reaches
// YARP's own forwarding at all.
public static class RateLimiterPolicies
{
    public const string PublishPolicy = "publish-token-bucket";
    public const string FollowPolicy = "follow-concurrency";
    public const string GeneralPolicy = "general-sliding-window";

    public static IServiceCollection AddPerTenantRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Read fresh from IConfiguration inside each policy's own partition
        // factory (invoked once per NEWLY-seen partition key, not once at
        // startup) rather than binding a single snapshot up front -- ADR-058's
        // own "changeable via configuration alone, no code deploy" requirement.
        // IConfiguration's own Get<T>() always reflects the current, possibly
        // hot-reloaded (appsettings.json's default reloadOnChange) values;
        // an ALREADY-provisioned partition's own limiter keeps its original
        // settings until that partition is next recreated (this library's
        // own idle-eviction, or this process restarting) -- an accepted,
        // standard characteristic of any partitioned rate limiter, not a gap
        // specific to this implementation.
        RateLimitingOptions CurrentLimits() => configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(PublishPolicy, httpContext =>
            {
                var key = TenantPartitionKey.Resolve(httpContext);
                var limits = CurrentLimits();
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.PublishTokenLimit,
                    TokensPerPeriod = limits.PublishTokensPerPeriod,
                    ReplenishmentPeriod = limits.PublishReplenishmentPeriod,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            options.AddPolicy(FollowPolicy, httpContext =>
            {
                var key = TenantPartitionKey.Resolve(httpContext);
                var limits = CurrentLimits();
                return RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = limits.FollowConcurrencyLimit,
                    QueueLimit = 0,
                });
            });

            options.AddPolicy(GeneralPolicy, httpContext =>
            {
                var key = TenantPartitionKey.Resolve(httpContext);
                var limits = CurrentLimits();
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limits.GeneralPermitLimit,
                    Window = limits.GeneralWindow,
                    SegmentsPerWindow = limits.GeneralSegmentsPerWindow,
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
