using EventStore.SchemaRegistry;

namespace EventStore.Rbac;

// ADR-067/044 -- registering a trust root publishes a reserved, platform-
// level event. Unlike role/permission grants, one AppTrustRoot IS a single,
// wholly-replaceable record per (AppId, IssuerDid) -- no set-membership
// mismatch, so this fits the generic EntityStoreRow fold's "replace field
// X" semantics directly, matching ADR-067's own {appId}:role:{roleId}-style
// example shape without needing a synthetic combined key.
public static class AppTrustRootRegisteredEventType
{
    public const string Name = "AppTrustRootRegistered";

    // Description stays out of "required" and is simply omitted from the
    // payload (not serialized as an explicit JSON null) when absent --
    // MaskingSchemaValidator's own node["type"]?.GetValue<string>() throws
    // on a multi-type array like ["string","null"] (found only by running
    // this; no earlier schema in this repo ever declared one), so a plain
    // "string" type is the only representable shape here.
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "IssuerDid": { "type": "string" },
            "Description": { "type": "string" }
          },
          "required": ["IssuerDid"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.IssuerDid",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "trustroot"), ct);
    }
}
