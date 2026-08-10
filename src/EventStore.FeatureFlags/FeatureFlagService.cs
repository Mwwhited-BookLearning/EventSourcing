using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.FeatureFlags;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.FeatureFlags;

// ADR-077 -- the write path: publish the reserved FeatureFlagSet event
// (real ActorId, hash-chained, lineage-traceable, exactly like any other
// business event), then fold it into FeatureFlagState in the SAME call --
// see FeatureFlagSetEventType's own comment for why this is synchronous
// rather than a cross-process Follow-based fold. The ONLY propagation
// delay in this whole mechanism is EventLogFeatureFlagConfigurationProvider's
// own poll of FeatureFlagState, exactly matching this item's own "toggling
// a flag is observable within one poll interval" exit criterion.
public class FeatureFlagService(EventStoreContext db, SchemaRegistryService schemaRegistry, PublishService publish)
{
    public async Task<PublishResult> SetFlagAsync(string appId, string key, string value, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await FeatureFlagSetEventType.EnsureRegisteredAsync(schemaRegistry, appId, ct);
        var payload = $$"""{ "Key": {{JsonSerializer.Serialize(key)}}, "Value": {{JsonSerializer.Serialize(value)}} }""";
        var result = await publish.PublishAsync(FeatureFlagSetEventType.Name, new PublishEventRequest(appId, 1, payload, null, null), user, ct);

        if (result is PublishResult.Accepted accepted)
            await UpsertStateAsync(appId, key, value, accepted.SequenceNumber, ct);

        return result;
    }

    private async Task UpsertStateAsync(string appId, string key, string value, long sequenceNumber, CancellationToken ct)
    {
        var existing = await db.FeatureFlags.SingleOrDefaultAsync(f => f.AppId == appId && f.Key == key, ct);
        if (existing is null)
        {
            db.FeatureFlags.Add(new FeatureFlagState { AppId = appId, Key = key, Value = value, LastAppliedSequenceNumber = sequenceNumber });
        }
        // A replayed/idempotent-retry publish (ADR-011's own EventId
        // short-circuit) returns the SAME sequence number as an earlier
        // call -- guard against regressing an already-newer row.
        else if (sequenceNumber > existing.LastAppliedSequenceNumber)
        {
            existing.Value = value;
            existing.LastAppliedSequenceNumber = sequenceNumber;
        }

        await db.SaveChangesAsync(ct);
    }
}
