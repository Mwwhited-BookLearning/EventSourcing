using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Erasure;

// ADR-057 -- the "special-purpose reactor" side effect RouterWorker performs
// against this event's own TargetEntityId, the same shape
// AuthorityDecisionResolver already established for authorityDecision:
// EntityErasureRequested folds into its own {appId}:entityerasurerequested:
// {targetEntityId} entity like any other registered event type (RouterWorker's
// ordinary fold, above this call), and this is the ADDITIONAL effect --
// destroying the target entity's DEK -- performed once that ordinary fold
// has already happened.
public static class EntityErasureResolver
{
    public static async Task ProcessAsync(ErasureKeyService erasureKeyService, EventStoreContext db, StoredEvent requestEvent, CancellationToken ct)
    {
        if (JsonNode.Parse(requestEvent.Payload) is not JsonObject payload)
            return;

        var targetEntityId = payload["TargetEntityId"]?.GetValue<string>();
        if (targetEntityId is null)
            return;

        await erasureKeyService.EraseAsync(targetEntityId, ct);

        // ADR-096/097 -- a real delete of a derived, rebuildable structure,
        // never cryptographic destruction: Shared-scope index tokens are
        // comparable across entities by construction, so there is no
        // per-entity key to destroy for them the way PerEntity-scope tokens
        // have. Deletes every row for this entity regardless of scope --
        // harmless for PerEntity-scope rows too, whose Token already became
        // permanently uncomputable the instant erasureKeyService.EraseAsync
        // above destroyed the owning DEK; this delete just tidies up the
        // now-inert rows in the same pass rather than leaving them
        // orphaned. The immutable StoredEvent/Payload itself is completely
        // unaffected either way (ADR-019's ChainHash/ADR-033's Merkle-tree
        // sync never touch this table).
        var sharedScopeEntries = await db.EncryptedFieldIndexEntries
            .Where(e => e.EntityId == targetEntityId)
            .ToListAsync(ct);
        if (sharedScopeEntries.Count > 0)
        {
            db.EncryptedFieldIndexEntries.RemoveRange(sharedScopeEntries);
            await db.SaveChangesAsync(ct);
        }
    }
}
