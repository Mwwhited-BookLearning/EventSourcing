using EventStore.SchemaRegistry;

namespace EventStore.Rbac;

// ADR-067 -- a direct per-actor permission grant, the same reserved-event
// treatment as RoleGrantedEventType.cs, scoped per (actor, permission) via
// a synthetic GrantKey for the identical reason (multiple actors can each
// hold the same permission independently; a single per-permission entity
// would let one actor's grant overwrite another's in the generic
// EntityStoreRow fold).
public static class PermissionGrantedEventType
{
    public const string Name = "PermissionGranted";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "ActorId": { "type": "string" },
            "Permission": { "type": "string" },
            "GrantKey": { "type": "string" }
          },
          "required": ["ActorId", "Permission", "GrantKey"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.GrantKey",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "userpermission"), ct);
    }
}
