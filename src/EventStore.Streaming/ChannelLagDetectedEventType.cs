using EventStore.SchemaRegistry;

namespace EventStore.Streaming;

// ADR-031 -- a slow-uploading producer publishes a reserved, system-owned
// "ChannelLagDetected" event, the same treatment ADR-020's (now-retired)
// EventUpcastFailed already established: fully queryable like any other
// type via ordinary Follow, not a bespoke alerting side-channel. Registered
// lazily, once per AppId, the first time this AppId's ingestion path
// actually needs it -- not seeded up front, since most deployments/AppIds
// may never trigger it.
public static class ChannelLagDetectedEventType
{
    public const string Name = "ChannelLagDetected";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "ChannelId": { "type": "string" },
            "ExpectedGapMicros": { "type": "number" },
            "ActualGapMicros": { "type": "number" }
          },
          "required": ["ChannelId", "ExpectedGapMicros", "ActualGapMicros"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.ChannelId",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null), ct);
    }
}
