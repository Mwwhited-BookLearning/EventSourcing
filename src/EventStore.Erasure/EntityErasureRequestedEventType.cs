using EventStore.SchemaRegistry;

namespace EventStore.Erasure;

// ADR-057 -- "Erasure is itself a permanent, auditable record -- an event,
// not a side effect": requesting erasure is an ordinary publish of this
// reserved, system-owned event type through the SAME Publish API any other
// event type uses, not a bespoke DELETE-shaped endpoint. Registered lazily,
// once per AppId, the same "not seeded up front" treatment
// ChannelLagDetectedEventType already established for ADR-031 -- triggered
// here from ErasureKeyService.GetOrCreateAsync's own first-DEK-for-this-AppId
// moment, so the type always exists before any encrypted data does, and
// therefore before an erasure request for that AppId could ever be
// meaningful.
public static class EntityErasureRequestedEventType
{
    public const string Name = "EntityErasureRequested";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "TargetEntityId": { "type": "string" }
          },
          "required": ["TargetEntityId"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.TargetEntityId",
            ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Publish", "erasure:request")],
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "entityerasurerequested"), ct);
    }
}
