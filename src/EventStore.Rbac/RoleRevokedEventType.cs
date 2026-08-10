using EventStore.SchemaRegistry;

namespace EventStore.Rbac;

// ADR-067's counterpart to RoleGrantedEventType.cs -- same payload/EntityId
// scheme (per (actor, role), via the same AssignmentKey), so a revocation
// folds into the SAME roleassignment entity its own grant did.
public static class RoleRevokedEventType
{
    public const string Name = "RoleRevoked";

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
