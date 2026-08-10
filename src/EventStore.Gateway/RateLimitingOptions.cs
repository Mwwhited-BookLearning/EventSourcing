namespace EventStore.Gateway;

// ADR-058 -- "limits themselves are deployment-time configuration, not
// hardcoded... which exact source is a build-time detail." Bound from an
// ordinary "RateLimiting" Microsoft.Extensions.Configuration section, one
// set of limits shared by every AppId (a per-AppId override table is not
// built here -- ADR-058 leaves the exact source as a build-time detail;
// this is the simplest one that satisfies "configuration, not code").
public class RateLimitingOptions
{
    // Token Bucket -- Inbox/publish traffic (ADR-058's own "absorbs a burst,
    // bounds sustained volume" reasoning).
    public int PublishTokenLimit { get; set; } = 20;
    public int PublishTokensPerPeriod { get; set; } = 5;
    public TimeSpan PublishReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);

    // Concurrency Limiter -- Follow/subscription-style long-lived
    // connections (bounds open SLOTS, not request rate).
    public int FollowConcurrencyLimit { get; set; } = 5;

    // Sliding Window -- everything else (ordinary GraphQL queries, registry/
    // RBAC/feature-flag admin calls) -- the general-purpose default.
    public int GeneralPermitLimit { get; set; } = 60;
    public TimeSpan GeneralWindow { get; set; } = TimeSpan.FromMinutes(1);
    public int GeneralSegmentsPerWindow { get; set; } = 6;
}
