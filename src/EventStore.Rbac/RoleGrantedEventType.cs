using EventStore.SchemaRegistry;

namespace EventStore.Rbac;

// ADR-067 -- a control-plane mutation (granting a role) publishes a
// reserved, platform-level event, the same treatment SchemaRegisteredEventType/
// EntityErasureRequestedEventType/ChannelLagDetectedEventType already
// established: an operator never registers this via PUT /registry, it's
// built into the platform. Registered lazily, once per AppId, on the first
// grant that AppId ever makes.
//
// EntityIdField deliberately deviates from ADR-067's own literal example
// ({appId}:role:{roleId}): that shape assumes one mutable record per Role,
// but a role can be granted to many actors independently, and the generic
// EntityStoreRow fold's patch-merge semantics model "replace field X,"
// never "add/remove an item from a set" -- multiple actors' grants of the
// SAME role would silently overwrite each other's own EntityStoreRow.
// Scoped per (actor, role) instead -- AssignmentKey is a synthetic,
// pre-combined field (EntityIdField only ever resolves ONE JSON pointer),
// computed by the publishing endpoint, not the caller. This build stage's
// own concrete choice, not literally what the ADR's own example shows.
public static class RoleGrantedEventType
{
    public const string Name = "RoleGranted";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "ActorId": { "type": "string" },
            "RoleName": { "type": "string" },
            "AssignmentKey": { "type": "string" }
          },
          "required": ["ActorId", "RoleName", "AssignmentKey"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.AssignmentKey",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "roleassignment"), ct);
    }
}
