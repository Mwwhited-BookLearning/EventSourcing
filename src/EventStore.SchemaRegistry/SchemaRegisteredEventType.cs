namespace EventStore.SchemaRegistry;

// ADR-067 -- a control-plane mutation (schema registration) publishes a
// reserved, platform-level event, the same treatment ADR-020's (now-retired)
// EventUpcastFailed and ADR-031's ChannelLagDetectedEventType already
// established: fully queryable/lineage-traceable like any other type, not a
// bespoke side-channel audit record. Registered lazily, once per AppId, the
// same "not seeded up front" precedent -- triggered from RegisterAsync's own
// first successful registration for that AppId, not at Host startup (no
// AppId is known then).
//
// Deliberately narrower than ADR-067's own literal "EventTypeDefinition...
// becomes a current-state read model folded from these events" text: this
// build stage does NOT rearchitect EventTypeDefinition's own write path onto
// this event (see docs/08-build-plan.md's own item 30 section for why --
// every prior build-plan item's own tests assume synchronous, immediately-
// consistent registration, which an async Router-fold would break). This
// event is a genuine, hash-chained, lineage-traceable audit record of "a
// registration happened" -- EventTypeDefinition itself stays a directly-
// written table, unchanged.
public static class SchemaRegisteredEventType
{
    public const string Name = "SchemaRegistered";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "EventTypeName": { "type": "string" },
            "Version": { "type": "number" }
          },
          "required": ["EventTypeName", "Version"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.EventTypeName",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "schema"), ct);
    }
}
