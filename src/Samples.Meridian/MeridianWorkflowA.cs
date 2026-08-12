using EventStore.SchemaRegistry;

namespace Samples.Meridian;

// Workflow A -- Document/Biometric Capture -> Verification
// (docs/domains/digital-identity-kyc/features/document-and-biometric-
// capture.md + customer-onboarding-and-identity-verification.md).
//
// **A real, load-bearing divergence from the second doc's own sequence
// diagram, found while scoping this workflow, not while writing it**:
// that doc shows the applicant publishing IdentityClaimSubmitted with a
// raw UCAN riding in AttestedClaims, and the ROUTER later performing an
// asynchronous OAuth Token Exchange call to DevIdp on the platform's own
// behalf to upgrade AuthorityStatus from "unattested" to "pending_review"
// once the exchange succeeds. No such logic exists anywhere in
// EventStore.Router/EventStore.Inbox (confirmed by search) -- the real,
// built UCAN/Token-Exchange mechanism is entirely CALLER-initiated (a
// client calls POST /connect/token itself, then uses the resulting JWT
// as an ordinary Bearer credential for whatever it does next), and every
// real UCAN issuer key must already be a registered AppTrustRoot or a
// seeded client identity -- there is no path for a genuinely first-time,
// walk-up applicant's own freshly-generated DID key to self-attest with
// zero prior registration (confirmed directly:
// `ADelegationWithNoProofRootedInAnUnregisteredKeyIsRejected` in the core
// suite). This workflow's own central "self-attestation" half is
// therefore modeled using the mechanism that IS real and already fully
// proven for exactly this shape -- ADR-035's credential-agnostic
// AttestedActorId/AttestedClaims (an opaque JSON blob the core engine
// never itself validates, exactly matching ADR-036's own "credential-
// agnostic" design), landing at AuthorityStatus "unattested," resolved
// directly by the SAME analyst authorityDecision reactor Workflow A's
// biometric-review step (below) and every Vitals workflow already reuse
// -- skipping the doc's own unbuilt "pending_review via successful
// exchange" intermediate stage, the identical "unattested -> analyst
// decision -> accepted" shape the core engine's own
// NonAuthoritativeCaptureScenarioAssertions already proves. The real
// UcanDelegation + OAuth Token Exchange mechanism this domain's OWN
// Workflow B (relying-party access) needs is genuinely exercised there
// instead -- see docs/domains/README.md's own build-status note.
public static class MeridianWorkflowA
{
    public const string AppId = "kyc";

    private const string IdentityDocumentUploadedSchema = """
        {
          "type": "object",
          "properties": {
            "ApplicantId": { "type": "string" },
            "DocumentType": { "type": "string" },
            "ExtractedDocumentNumber": { "type": "string", "x-masking": { "requiredClaim": "identity:pii-read", "strategy": "PartialReveal", "showFirst": 1, "showLast": 0 } }
          },
          "required": ["ApplicantId", "DocumentType", "ExtractedDocumentNumber"]
        }
        """;

    private const string BiometricCaptureRecordedSchema = """
        {
          "type": "object",
          "properties": {
            "ApplicantId": { "type": "string" },
            "CaptureType": { "type": "string" },
            "LivenessCheckResult": { "type": "string" },
            "LivenessConfidence": { "type": "number" }
          },
          "required": ["ApplicantId", "CaptureType", "LivenessCheckResult", "LivenessConfidence"]
        }
        """;

    private const string IdentityClaimSubmittedSchema = """
        {
          "type": "object",
          "properties": {
            "ApplicantId": { "type": "string" },
            "Did": { "type": "string" },
            "ClaimedLegalName": { "type": "string", "x-masking": { "requiredClaim": "identity:pii-read", "strategy": "PartialReveal", "showFirst": 1, "showLast": 0 } },
            "DateOfBirth": { "type": "string", "x-masking": { "requiredClaim": "identity:pii-read", "strategy": "PartialReveal", "showFirst": 0, "showLast": 0 } },
            "DocumentType": { "type": "string" }
          },
          "required": ["ApplicantId", "Did", "ClaimedLegalName", "DateOfBirth", "DocumentType"]
        }
        """;

    public static async Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        // EntityType: "ApplicantIdentity" on all three -- explicit, so
        // documents/biometric capture/identity claim all accumulate onto
        // the ONE entity kyc:ApplicantIdentity:{ApplicantId}, the same
        // "OrderPlaced/OrderShipped" reasoning Vitals' own Patient entity
        // already established. ChangeKind Partial on all three, not the
        // customer-onboarding doc's own unstated default -- IdentityClaimSubmitted
        // must merge onto whatever IdentityDocumentUploaded/
        // BiometricCaptureRecorded already contributed, never replace it.
        await registry.RegisterAsync("IdentityDocumentUploaded", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: IdentityDocumentUploadedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.ApplicantId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "ApplicantIdentity"), ct);

        await registry.RegisterAsync("BiometricCaptureRecorded", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: BiometricCaptureRecordedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.ApplicantId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "ApplicantIdentity"), ct);

        await registry.RegisterAsync("IdentityClaimSubmitted", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: IdentityClaimSubmittedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.ApplicantId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "ApplicantIdentity"), ct);

        await MeridianSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "identity:review", ct);
    }
}
