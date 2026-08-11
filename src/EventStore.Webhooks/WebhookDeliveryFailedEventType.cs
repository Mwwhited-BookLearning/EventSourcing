using EventStore.SchemaRegistry;

namespace EventStore.Webhooks;

// ADR-060 -- exhausted delivery retries dead-letter as a reserved,
// platform-owned event, the same "make the failure an inspectable record"
// treatment ADR-020's (now-retired) EventUpcastFailed already established:
// queryable through the ordinary Lineage API, never just an operator log
// line. Registered lazily, once per AppId, on the first delivery this
// AppId's own subscriptions ever exhaust.
//
// FailureKey (a synthetic, pre-combined "{SubscriptionId}:{SequenceNumber}"
// field) is the same "one entity per fact, not per mutable record" choice
// RoleGrantedEventType's own AssignmentKey already established -- a bare
// SubscriptionId would let a second, later exhausted delivery for the SAME
// subscription silently overwrite the first failure's own EntityStoreRow.
public static class WebhookDeliveryFailedEventType
{
    public const string Name = "WebhookDeliveryFailed";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "SubscriptionId": { "type": "string" },
            "TargetSequenceNumber": { "type": "integer" },
            "Attempts": { "type": "integer" },
            "LastError": { "type": "string" },
            "FailureKey": { "type": "string" }
          },
          "required": ["SubscriptionId", "TargetSequenceNumber", "Attempts", "LastError", "FailureKey"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.FailureKey",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "webhookdeliveryfailure"), ct);
    }
}
