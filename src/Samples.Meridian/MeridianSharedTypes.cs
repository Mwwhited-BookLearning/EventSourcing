using EventStore.Domain.SchemaRegistry;
using EventStore.SchemaRegistry;

namespace Samples.Meridian;

// The shared, reserved-name "authorityDecision" reactor (EventStore.
// Router's AuthorityDecisionResolver) is reused across every Meridian
// workflow that needs a human decision on an already-captured record --
// Workflow A's analyst review of an identity claim, and (should this
// domain's own Workflow C need it later) a compliance officer's SAR
// decision. Same "ensure at least this claim" union-of-claims mechanism
// Samples.Vitals.VitalsSharedTypes already established -- deliberately
// duplicated here, not shared across the two sample projects: Vitals and
// Meridian are independent proving-ground applications a reader might
// look at one without the other, not two halves of one shared library.
public static class MeridianSharedTypes
{
    public const string AuthorityDecisionType = "authorityDecision";

    private const string AuthorityDecisionSchema = """
        {
          "type": "object",
          "properties": {
            "targetEventId": { "type": "string" },
            "decision": { "type": "string" },
            "decidingActorId": { "type": "string" },
            "reason": { "type": "string" }
          },
          "required": ["targetEventId", "decision", "decidingActorId"]
        }
        """;

    public static async Task EnsureAuthorityDecisionRegisteredAsync(SchemaRegistryService registry, string appId, string requiredPublishClaim, CancellationToken ct = default)
    {
        var active = await registry.GetActiveAsync(appId, AuthorityDecisionType, ct);
        var existingClaims = active?.RequiredClaims
            .Where(c => c.Direction == ClaimDirection.Publish)
            .Select(c => c.Claim)
            .ToList() ?? [];
        if (existingClaims.Contains(requiredPublishClaim))
            return;

        var claims = existingClaims.Append(requiredPublishClaim).Distinct()
            .Select(c => new RequiredClaimRequest("Publish", c)).ToList();

        await registry.RegisterAsync(AuthorityDecisionType, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: AuthorityDecisionSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.targetEventId", ParentValidationMode: "Permissive",
            RequiredClaims: claims, UpcastFromPrevious: null, DowncastToPrevious: null), ct);
    }
}
