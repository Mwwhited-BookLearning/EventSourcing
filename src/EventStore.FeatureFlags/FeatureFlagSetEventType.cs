using EventStore.SchemaRegistry;

namespace EventStore.FeatureFlags;

// ADR-077/067 -- toggling a feature flag publishes a reserved, hash-chained
// event, the same treatment ADR-067's own SchemaRegistered/RoleGranted/etc.
// already established: fully queryable/lineage-traceable, carrying the
// real ActorId of the operator who made the change, not a bespoke,
// unaudited admin table. Registered lazily, once per AppId, on the first
// flag ever set for that AppId.
//
// Unlike ADR-067's RBAC events (folded cross-process by EventStore.DevIdp,
// a separate identity-provider process), FeatureFlagState is read by the
// SAME Host process that publishes this event -- so the fold is
// synchronous, in the same call (FeatureFlagService.SetFlagAsync), the
// same posture SchemaRegisteredEventType's own EventTypeDefinition write
// already established. No cross-process Follow subscription is needed or
// built here.
public static class FeatureFlagSetEventType
{
    public const string Name = "FeatureFlagSet";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "Key": { "type": "string" },
            "Value": { "type": "string" }
          },
          "required": ["Key", "Value"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Key",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "featureflag"), ct);
    }
}
