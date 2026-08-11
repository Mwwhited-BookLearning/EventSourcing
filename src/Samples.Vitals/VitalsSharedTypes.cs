using EventStore.Domain.SchemaRegistry;
using EventStore.SchemaRegistry;

namespace Samples.Vitals;

// The "authorityDecision" reserved-name mechanism (EventStore.Router's
// AuthorityDecisionResolver) is shared across every Vitals workflow that
// needs a human decision on an already-captured record -- Workflow A's
// investigator countersignature and Workflow B's CRF sign-off both
// target the SAME AppId ("trial1") and both need this type registered
// with the identical RequiredSignature. Idempotent (checks GetActiveAsync
// first) so whichever workflow's own registration runs first "wins,"
// the same lazy-registration posture EventStore.Streaming's
// ChannelLagDetectedEventType/EventStore.ExpectedResponse's
// ExpectedResponseMissingEventType already establish for a shared,
// cross-workflow type -- but this one is an ORDINARY, explicitly-
// registered event type (docs/domains/clinical-trials-device-telemetry/
// features/adverse-event-capture-and-review.md's own Gherkin Background
// registers it explicitly), not a platform-reserved one; the "ensure"
// shape is reused here purely to avoid a duplicate-registration
// collision across sibling workflows, not because the type itself is
// reserved.
//
// docs/domains/clinical-trials-device-telemetry/README.md's own
// "Workflows" framing describes Workflow A's approval step as a
// "sibling ConsentApprovalResolver" -- the real build instead reuses this
// exact, already-generic, already-tested reactor directly (it resolves
// purely by targetEventId, with zero knowledge of what entity/event type
// the target actually is), since EventStore.Router's own fold primitives
// (FoldAsync/FoldLiveAsync/SplitByConformance) are internal and cannot
// be called from a separate sample assembly -- see docs/domains/README.md's
// own "Sample application build status" note for the full reasoning.
public static class VitalsSharedTypes
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

    // Each Vitals workflow's own PrincipalInvestigator-shaped decision
    // (consent approval, AE review, IONM interpretation, ...) needs its
    // OWN Publish-direction claim on this one shared type -- ADR-050's
    // OR-of-list semantics (any ONE listed claim satisfies the gate)
    // means every workflow just adds its own claim to the list rather
    // than needing a separate type per workflow (which the reserved,
    // hardcoded "authorityDecision" reactor name wouldn't allow anyway).
    // "Ensure at least this claim is present" rather than a one-shot
    // "ensure registered" -- ADR-046's Role bundles are never actually
    // narrowed by this union (a PrincipalInvestigator's real role bundle
    // already carries every decision claim this domain names, per each
    // workflow's own Background), so this only ever widens who CAN
    // decide, never lets an under-privileged caller through.
    public static async Task EnsureAuthorityDecisionRegisteredAsync(SchemaRegistryService registry, string appId, string requiredPublishClaim, CancellationToken ct = default)
    {
        var active = await registry.GetActiveAsync(appId, AuthorityDecisionType, ct);
        var existingClaims = active?.RequiredClaims
            .Where(c => c.Direction == ClaimDirection.Publish)
            .Select(c => c.Claim)
            .ToList() ?? [];
        if (existingClaims.Contains(requiredPublishClaim))
            return; // already covers this workflow's own decision claim -- no new version needed

        var claims = existingClaims.Append(requiredPublishClaim).Distinct()
            .Select(c => new RequiredClaimRequest("Publish", c)).ToList();

        await registry.RegisterAsync(AuthorityDecisionType, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: AuthorityDecisionSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.targetEventId", ParentValidationMode: "Permissive",
            RequiredClaims: claims, UpcastFromPrevious: null, DowncastToPrevious: null,
            RequiredSignature: new RequiredSignatureRequest(["urn:trial:step-up"], 300)), ct);
    }
}
