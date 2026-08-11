using EventStore.SchemaRegistry;

namespace EventStore.ExpectedResponse;

// ADR-094 -- a tracker row past its own DeadlineAt with no matching response
// yet publishes a reserved, system-owned "ExpectedResponseMissing" event,
// the same treatment ADR-020's (now-retired) EventUpcastFailed and ADR-031's
// ChannelLagDetected already established: fully queryable like any other
// type via ordinary Follow, never a bespoke alerting side-channel. Never
// registered via PUT /registry/{event-type} -- registered lazily,
// once per AppId, the first time that AppId's own escalation path actually
// needs it.
public static class ExpectedResponseMissingEventType
{
    public const string Name = "ExpectedResponseMissing";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "RequestEventId": { "type": "string" },
            "RequestEventType": { "type": "string" },
            "ExpectedResponseEventType": { "type": "string" },
            "DeadlineAt": { "type": "string" }
          },
          "required": ["RequestEventId", "RequestEventType", "ExpectedResponseEventType", "DeadlineAt"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.RequestEventId",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null), ct);
    }
}
