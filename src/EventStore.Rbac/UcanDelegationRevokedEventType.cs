using EventStore.SchemaRegistry;

namespace EventStore.Rbac;

// ADR-104 -- the revocation half of the delegated-UCAN-grant audit trail,
// symmetric with ADR-107's own ucanDelegationIssued (EventStore.Ucan/
// UcanDelegationIssuedEventType.cs) but a genuinely platform-RESERVED
// event (ADR-067's convention), not an ordinary caller-registered type --
// ADR-104's own Decision text explicitly says this "reuses ADR-067's
// existing 'control-plane action as a reserved event' convention," unlike
// ADR-107's deliberate choice NOT to reserve issuance. Lives in
// EventStore.Rbac (not EventStore.Ucan, which has zero
// SchemaRegistryService/PublishService dependency by design) alongside
// AppTrustRootRegisteredEventType -- already the natural home for
// UCAN-adjacent control-plane events, per that file's own ADR-044 citation.
//
// EntityIdField/EntityType both target "$.GrantRef" / "ucandelegationissued"
// -- folds onto the SAME entity ucanDelegationIssued's own EntityId
// produces, per ADR-107's own forward-pointing comment ("the same
// identifier a future UcanDelegationRevoked event would key its own
// revocation lookup on").
public static class UcanDelegationRevokedEventType
{
    public const string Name = "UcanDelegationRevoked";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "GrantRef": { "type": "string" },
            "RevokedAt": { "type": "string" }
          },
          "required": ["GrantRef", "RevokedAt"]
        }
        """;

    public static async Task EnsureRegisteredAsync(SchemaRegistryService schemaRegistry, string appId, CancellationToken ct)
    {
        if (await schemaRegistry.GetActiveAsync(appId, Name, ct) is not null)
            return;

        await schemaRegistry.RegisterAsync(Name, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.GrantRef",
            ParentValidationMode: "Permissive", RequiredClaims: null,
            UpcastFromPrevious: null, DowncastToPrevious: null,
            EntityType: "ucandelegationissued"), ct);
    }

    public static string BuildPayload(Guid grantRef, DateTimeOffset revokedAt) =>
        System.Text.Json.JsonSerializer.Serialize(new { GrantRef = grantRef.ToString(), RevokedAt = revokedAt.ToString("O") });
}
