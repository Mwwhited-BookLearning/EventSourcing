using EventStore.Domain.SchemaRegistry;
using EventStore.SchemaRegistry;

namespace Samples.Meridian;

// The shared, reserved-name "authorityDecision" reactor (EventStore.
// Router's AuthorityDecisionResolver) is reused across every Meridian
// workflow that needs a human decision on an already-captured record --
// Workflow A's analyst review of an identity claim, and Workflow C's
// own compliance-officer SAR decision (MeridianWorkflowC.cs's own
// EnsureAuthorityDecisionRegisteredAsync call with "identity:aml-review").
// Same "ensure at least this claim" union-of-claims mechanism
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

    public static Task EnsureAuthorityDecisionRegisteredAsync(SchemaRegistryService registry, string appId, string requiredPublishClaim, CancellationToken ct = default) =>
        registry.EnsureClaimOnReservedTypeAsync(appId, AuthorityDecisionType, AuthorityDecisionSchema, requiredPublishClaim, ct: ct);
}
