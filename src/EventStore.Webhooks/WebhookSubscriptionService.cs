using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EventStore.Domain.Webhooks;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Webhooks;

// ADR-060 -- the registration write path: freezes FixedClaimsSnapshot
// ONCE, from the registering caller's OWN current claims, exactly the same
// "claims fixed for the lifetime of a Follow connection" rule ADR-009
// already applies -- applied here to a subscription's lifetime instead.
// Never re-evaluated afterward; a claim later granted to or revoked from
// the registering caller has no effect on an already-registered
// subscription (verified by RegisteringASubscriptionFreezesItsClaimSnapshotOnce).
public class WebhookSubscriptionService(EventStoreContext db)
{
    public async Task<WebhookSubscription> RegisterAsync(
        string appId, string targetUrl, IReadOnlyList<string> eventTypes, string? signingSecret, ClaimsPrincipal registeringCaller,
        string? outboundAdapterKey = null, CancellationToken ct = default)
    {
        var subscription = new WebhookSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            AppId = appId,
            TargetUrl = targetUrl,
            SigningSecret = signingSecret ?? GenerateSecret(),
            EventTypes = eventTypes.ToList(),
            FixedClaimsSnapshot = SnapshotClaims(registeringCaller),
            Active = true,
            RegisteredAt = DateTimeOffset.UtcNow,
            OutboundAdapterKey = outboundAdapterKey,
        };

        db.WebhookSubscriptions.Add(subscription);
        db.WebhookDeliveryCursors.Add(new WebhookDeliveryCursor { SubscriptionId = subscription.SubscriptionId });
        await db.SaveChangesAsync(ct);
        return subscription;
    }

    public Task<WebhookSubscription?> GetAsync(Guid subscriptionId, CancellationToken ct = default) =>
        db.WebhookSubscriptions.SingleOrDefaultAsync(s => s.SubscriptionId == subscriptionId, ct);

    // ADR-093 -- opens a rotation overlap window: the still-valid current
    // secret becomes PreviousSigningSecret, a freshly generated secret
    // becomes the new SigningSecret. WebhookOutboxPump picks up the
    // change on its own very next tick (it re-reads WebhookSubscriptions
    // every RunOnceAsync, no cache to invalidate) and starts emitting
    // dual signatures immediately -- a delivery already in flight when
    // this call lands was already signed under the OLD secret alone, and
    // a receiver holding only that old secret still verifies it, exactly
    // per this ADR's own exit criterion.
    public async Task<string> RotateSigningSecretAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscription = await db.WebhookSubscriptions.SingleAsync(s => s.SubscriptionId == subscriptionId, ct);
        var newSecret = GenerateSecret();
        subscription.PreviousSigningSecret = subscription.SigningSecret;
        subscription.SigningSecret = newSecret;
        await db.SaveChangesAsync(ct);
        return newSecret;
    }

    // ADR-093 -- ends the overlap window: an ops team calls this once
    // they're confident every receiver has picked up the new secret
    // (this ADR's own "rotation cadence stays ops-configurable" —
    // WHEN to call this is deployment policy, not a framework timer).
    // After this, WebhookSigner.Sign never fires (previousSigningSecret
    // null again) and only the current secret verifies.
    public async Task DiscardPreviousSigningSecretAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscription = await db.WebhookSubscriptions.SingleAsync(s => s.SubscriptionId == subscriptionId, ct);
        subscription.PreviousSigningSecret = null;
        await db.SaveChangesAsync(ct);
    }

    // ADR-008's own "type:value" claim string, the same primitive
    // RequiredClaimEvaluator.HasClaim parses -- a plain JSON string array
    // is enough to reconstruct an equivalent hasClaim(claim) check later,
    // without needing a live ClaimsPrincipal (there isn't one -- delivery
    // happens on WebhookOutboxPump's own background tick, long after the
    // registering HTTP request has completed).
    public static string SnapshotClaims(ClaimsPrincipal user) =>
        JsonSerializer.Serialize(user.Claims.Select(c => $"{c.Type}:{c.Value}").ToList());

    public static Func<string, bool> BuildHasClaim(string fixedClaimsSnapshot)
    {
        var claims = JsonSerializer.Deserialize<List<string>>(fixedClaimsSnapshot) ?? [];
        return claim => claims.Contains(claim);
    }

    private static string GenerateSecret() => $"whsec_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
}
