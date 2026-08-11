using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.Replication;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Replication;

// ADR-061 -- the write path: publish the reserved AllowedRegionsSet event
// (real ActorId, hash-chained, lineage-traceable), then fold it into
// AppResidencyPolicy in the SAME call -- see AllowedRegionsSetEventType's
// own comment for why this is synchronous, matching FeatureFlagService's
// exact precedent.
public class AppResidencyPolicyService(EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publish)
{
    public async Task<PublishResult> SetAllowedRegionsAsync(string appId, List<string> allowedRegions, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await AllowedRegionsSetEventType.EnsureRegisteredAsync(schemaRegistry, appId, ct);
        var payload = JsonSerializer.Serialize(new { AppId = appId, AllowedRegions = allowedRegions });
        var result = await publish.PublishAsync(AllowedRegionsSetEventType.Name, new PublishEventRequest(appId, 1, payload, null, null), user, ct);

        if (result is PublishResult.Accepted accepted)
            await UpsertPolicyAsync(appId, allowedRegions, accepted.SequenceNumber, ct);

        return result;
    }

    // Unconstrained (no row at all) is the default -- ADR-061's own "purely
    // additive" text. Returned as a plain list rather than an Option-ish
    // wrapper since the caller (PeerSyncWorker) only ever asks "is this
    // region in the allowed list, if one exists" -- an empty list already
    // means unconstrained without a separate null-check.
    public async Task<Dictionary<string, List<string>>> GetAllPoliciesAsync(CancellationToken ct = default) =>
        await db.AppResidencyPolicies.AsNoTracking().ToDictionaryAsync(p => p.AppId, p => p.AllowedRegions, ct);

    private async Task UpsertPolicyAsync(string appId, List<string> allowedRegions, long sequenceNumber, CancellationToken ct)
    {
        var existing = await db.AppResidencyPolicies.SingleOrDefaultAsync(p => p.AppId == appId, ct);
        if (existing is null)
        {
            db.AppResidencyPolicies.Add(new AppResidencyPolicy { AppId = appId, AllowedRegions = allowedRegions, LastAppliedSequenceNumber = sequenceNumber });
        }
        // A replayed/idempotent-retry publish (ADR-011's own EventId
        // short-circuit) returns the SAME sequence number as an earlier
        // call -- guard against regressing an already-newer row.
        else if (sequenceNumber > existing.LastAppliedSequenceNumber)
        {
            existing.AllowedRegions = allowedRegions;
            existing.LastAppliedSequenceNumber = sequenceNumber;
        }

        await db.SaveChangesAsync(ct);
    }
}
