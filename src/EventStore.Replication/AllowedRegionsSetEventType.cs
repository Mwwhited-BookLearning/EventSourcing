using EventStore.SchemaRegistry;

namespace EventStore.Replication;

// ADR-061/067 -- constraining an AppId's residency publishes a reserved,
// hash-chained event, the same treatment ADR-077's FeatureFlagSet already
// established for a simple per-AppId configuration value: fully
// queryable/lineage-traceable, carrying the real ActorId of the operator
// who made the change, not a bespoke, unaudited config table. Folded
// SYNCHRONOUSLY in the same call (AppResidencyPolicyService.SetAllowed
// RegionsAsync) -- AppResidencyPolicy is read by the SAME process
// (PeerSyncWorker) that publishes this event, the same posture
// FeatureFlagState's own fold already established, unlike ADR-067's own
// RBAC events which need a cross-process Follow fold into DevIdp.
// Registered lazily, once per AppId, on the first residency policy that
// AppId ever sets.
//
// AppId is redundantly carried inside the payload (not just the envelope)
// so EntityIdField can resolve a per-AppId entity -- there is exactly one
// residency policy per AppId, not a per-key collection the way FeatureFlagSet's
// own Key differentiates multiple flags for one AppId.
public static class AllowedRegionsSetEventType
{
    public const string Name = "AllowedRegionsSet";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "AppId": { "type": "string" },
            "AllowedRegions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["AppId", "AllowedRegions"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.AppId",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "appresidencypolicy"), ct);
    }
}
