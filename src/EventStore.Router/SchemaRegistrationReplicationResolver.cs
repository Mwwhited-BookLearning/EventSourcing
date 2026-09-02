using System.Text.Json;
using EventStore.Domain.EventLog;
using EventStore.Persistence;
using EventStore.SchemaRegistry;

namespace EventStore.Router;

// docs/10-open-questions.md row 1, resolved this pass: ADR-033's own
// mechanism replicates every ORDINARY event to every peer via the same
// fold pipeline (RouterWorker.ProcessEventAsync's own generic path), but
// EventTypeDefinition itself was never rearchitected onto that pipeline
// (SchemaRegisteredEventType.cs's own header comment states this
// deliberately, for a real reason -- every prior build-plan item's tests
// assume synchronous, immediately-consistent LOCAL registration, which a
// generic Router-fold of this reserved type would break). This resolver
// is deliberately NOT that generic rearchitecture -- it's the same
// "special-purpose reactor, one narrow event type" shape
// AuthorityDecisionResolver/EntityErasureResolver already establish
// (RouterWorker's own "ordinary fold above, additional reactor effect
// here" convention), scoped to exactly one case: a SchemaRegistered
// notification that arrived from ANOTHER site via peer-sync, never this
// site's own locally-originated copy (which SchemaRegistryService.
// RegisterAsync already applied synchronously and directly, same as
// always -- RouterWorker's own OriginId comparison, not this resolver,
// is what keeps the two paths from ever double-applying).
public static class SchemaRegistrationReplicationResolver
{
    public static async Task ProcessAsync(SchemaRegistryService schemaRegistry, StoredEvent notificationEvent, CancellationToken ct)
    {
        ReplicatedSchemaRegistration? replicated;
        try
        {
            replicated = JsonSerializer.Deserialize<ReplicatedSchemaRegistration>(notificationEvent.Payload);
        }
        catch (JsonException)
        {
            // A pre-this-pass peer's own narrower {EventTypeName, Version}-
            // only notification (or any other malformed payload) -- there is
            // nothing here to fold; leave it as the ordinary audit-only
            // record it already is, never a hard failure that would take
            // this tick's whole batch down.
            return;
        }
        if (replicated is null || string.IsNullOrEmpty(replicated.JsonSchema))
            return;

        await schemaRegistry.ApplyReplicatedRegistrationAsync(replicated, ct);
    }
}
