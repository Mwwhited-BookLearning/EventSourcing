using EventStore.SchemaRegistry;

namespace Samples.Vitals;

// Workflow A -- Enrollment & Consent
// (docs/domains/clinical-trials-device-telemetry/features/patient-
// enrollment-and-informed-consent.md). Registers this workflow's own two
// event types plus this AppId's shared "authorityDecision" reactor
// (VitalsSharedTypes), gated on the "consent:approve" claim
// ("PrincipalInvestigator"'s own role bundle, per that doc's Background)
// -- a SiteCoordinator (patient:enroll, consent:capture only) can never
// satisfy it, the standing role separation the feature doc's own Gherkin
// tests directly.
public static class VitalsWorkflowA
{
    public const string AppId = "trial1";

    // LegalName/DateOfBirth are optional -- neither Workflow A's own nor
    // Workflow B's own scenarios ever set them, so PatientScreened's
    // existing behavior there is completely unaffected; only Workflow C's
    // own erasure scenarios (trial-data-export-and-subject-rights.md)
    // publish them, for a different, non-continuity subject. Classified
    // under "clearance:phi" (the same claim ErasureScenarioAssertions'
    // own PHI demo and this repo's seeded "clinician-spa-client" already
    // use), never a domain-invented claim name.
    private const string PatientScreenedSchema = """
        {
          "type": "object",
          "properties": {
            "SubjectId": { "type": "string" },
            "SiteId": { "type": "string" },
            "ProtocolId": { "type": "string" },
            "ScreeningDate": { "type": "string" },
            "EligibilityStatus": { "type": "string" },
            "LegalName": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED", "regulatoryClassification": "PHI" } },
            "DateOfBirth": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED", "regulatoryClassification": "PHI" } }
          },
          "required": ["SubjectId", "SiteId", "EligibilityStatus"]
        }
        """;

    private const string InformedConsentCapturedSchema = """
        {
          "type": "object",
          "properties": {
            "SubjectId": { "type": "string" },
            "ConsentVersion": { "type": "string" },
            "ConsentObtainedAt": { "type": "string" },
            "WitnessActorId": { "type": "string" }
          },
          "required": ["SubjectId", "ConsentVersion", "ConsentObtainedAt", "WitnessActorId"]
        }
        """;

    public static async Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        // EntityType: "Patient" on BOTH -- explicit, not left to the
        // per-type default (ADR-021's own EntityType default is each
        // type's OWN normalized Name, which would otherwise fold
        // PatientScreened and InformedConsentCaptured into two SEPARATE
        // entities instead of one accumulating patient record; the same
        // OrderPlaced/OrderShipped distinction that field exists to make
        // possible, applied here).
        await registry.RegisterAsync("PatientScreened", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: PatientScreenedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.SubjectId", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Publish", "patient:enroll")],
            UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Patient"), ct);

        await registry.RegisterAsync("InformedConsentCaptured", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: InformedConsentCapturedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.SubjectId", ParentValidationMode: "Permissive",
            RequiredClaims: [new RequiredClaimRequest("Publish", "consent:capture")],
            UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Patient"), ct);

        await VitalsSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "consent:approve", ct);
    }
}
