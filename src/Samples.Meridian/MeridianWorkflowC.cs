using EventStore.SchemaRegistry;

namespace Samples.Meridian;

// Workflow C -- Ongoing Screening & SAR Escalation
// (docs/domains/digital-identity-kyc/features/periodic-screening-and-sar-
// escalation.md). Unlike every other workflow's own feature doc, this
// one predates ADR-079's resolution and was written to introduce NO new
// framework mechanism at all -- confirmed while building it: every
// mechanism here (RequiredClaims' OR-of-list, the shared authorityDecision
// reactor, RequiredSignature step-up) is exactly as real and already-
// proven as the doc itself claims, no divergence found.
public static class MeridianWorkflowC
{
    public const string AppId = MeridianWorkflowA.AppId;

    private const string SanctionsScreeningPerformedSchema = """
        {
          "type": "object",
          "properties": {
            "ApplicantId": { "type": "string" },
            "ScreeningDate": { "type": "string" },
            "ListsChecked": { "type": "array", "items": { "type": "string" } },
            "MatchFound": { "type": "boolean" },
            "MatchConfidence": { "type": "number" },
            "MatchedName": { "type": "string", "x-masking": { "requiredClaim": "identity:aml-review", "strategy": "PartialReveal" } },
            "MatchedListEntryId": { "type": "string", "x-masking": { "requiredClaim": "identity:aml-review", "strategy": "FixedValue" } }
          },
          "required": ["ApplicantId", "ScreeningDate", "ListsChecked", "MatchFound"]
        }
        """;

    private const string SarFilingRecordedSchema = """
        {
          "type": "object",
          "properties": {
            "ApplicantId": { "type": "string" },
            "TargetScreeningEventId": { "type": "string" },
            "FilingReferenceId": { "type": "string" },
            "Narrative": { "type": "string", "x-masking": { "requiredClaim": "identity:aml-review", "strategy": "FixedValue" } }
          },
          "required": ["ApplicantId", "TargetScreeningEventId", "FilingReferenceId", "Narrative"]
        }
        """;

    public static async Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        // Both Partial, EntityType "ApplicantIdentity" explicit -- fold
        // onto the SAME entity Workflow A's documents/biometric/claim
        // already accumulate onto, the identical reasoning that domain's
        // own registration already established.
        await registry.RegisterAsync("SanctionsScreeningPerformed", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: SanctionsScreeningPerformedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.ApplicantId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "ApplicantIdentity"), ct);

        await registry.RegisterAsync("SarFilingRecorded", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: SarFilingRecordedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.ApplicantId", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Publish", "identity:aml-review")],
            UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "ApplicantIdentity",
            RequiredSignature: new RequiredSignatureRequest(["urn:kyc:acr:step-up"], 300)), ct);

        await MeridianSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "identity:aml-review", ct);
    }
}
