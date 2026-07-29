[← Libraries index](../README.md)

# ASP.NET Core Rate Limiting middleware (dotnet)

**What it's for:** first-party, in-the-box request rate limiting for
ASP.NET Core (`System.Threading.RateLimiting`/`Microsoft.AspNetCore.
RateLimiting`, .NET 7+) — four built-in algorithms (Fixed Window,
Sliding Window, Token Bucket, Concurrency Limiter), per-partition
keying, `429` rejection, and `Retry-After` headers, with no third-party
package required.

**Why bought, not built:** rate limiting is a solved, well-understood
problem shipped directly in the framework this design already depends
on everywhere — reaching for a third-party limiter (or hand-rolling a
token bucket) would duplicate something already first-party and
directly composable with `YARP` (`ADR-049`), since `YARP` *is* an
ASP.NET Core app.

## General usage

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-tenant-publish", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: context.User.FindFirst("appId")?.Value ?? "anonymous",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                TokensPerPeriod = 100,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

app.MapPost("/publish/{eventType}", PublishEndpoint.Handle)
   .RequireRateLimiting("per-tenant-publish");
```

## Where this project uses it

`ADR-058` — per-`AppId` rate limiting at the API Gateway (`ADR-049`,
YARP), Token Bucket for publish, Concurrency Limiter for long-lived
GraphQL Subscription/Follow-style connections, Sliding Window for
ordinary query/publish bursts.

## Links

- [learn.microsoft.com/aspnet/core/performance/rate-limit](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
