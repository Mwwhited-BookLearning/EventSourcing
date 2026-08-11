using System.Text.Json.Nodes;
using EventStore.Domain.EventLog;

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
    public static async Task ProcessAsync(ErasureKeyService erasureKeyService, StoredEvent requestEvent, CancellationToken ct)
    {
        if (JsonNode.Parse(requestEvent.Payload) is not JsonObject payload)
            return;

        var targetEntityId = payload["TargetEntityId"]?.GetValue<string>();
        if (targetEntityId is null)
            return;

        await erasureKeyService.EraseAsync(targetEntityId, ct);
    }
}
