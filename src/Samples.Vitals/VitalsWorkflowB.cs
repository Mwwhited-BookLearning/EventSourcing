using EventStore.SchemaRegistry;

namespace Samples.Vitals;

// Workflow B -- Device Monitoring -> Adverse Event Review, upstream half
// (docs/domains/clinical-trials-device-telemetry/features/device-
// onboarding-and-continuous-monitoring.md) and downstream half
// (features/adverse-event-capture-and-review.md). Neither doc's own
// Gherkin gates DeviceOnboarded/AdverseEventReported behind a domain-
// specific RequiredClaims entry -- both explicitly note "every request
// carries an ordinary Bearer token with events:publish... AuthorityStatus
// is a separate trust axis from that scope check" -- so, unlike Workflow
// A's PatientScreened/InformedConsentCaptured, neither is registered with
// one here either.
public static class VitalsWorkflowB
{
    public const string AppId = VitalsWorkflowA.AppId;

    private const string DeviceOnboardedSchema = """
        {
          "type": "object",
          "properties": {
            "DeviceId": { "type": "string" },
            "DeviceModel": { "type": "string" },
            "InterfaceKind": { "type": "string" },
            "PairedToSubjectId": { "type": "string" },
            "SiteId": { "type": "string" }
          },
          "required": ["DeviceId", "DeviceModel", "InterfaceKind", "PairedToSubjectId", "SiteId"]
        }
        """;

    private const string AdverseEventReportedSchema = """
        {
          "type": "object",
          "properties": {
            "AeId": { "type": "string" },
            "SubjectId": { "type": "string" },
            "SiteId": { "type": "string" },
            "Description": { "type": "string" },
            "Severity": { "type": "string" },
            "SeriousAdverseEvent": { "type": "boolean" },
            "CausalityAssessment": { "type": "string" }
          },
          "required": ["AeId", "SubjectId", "Severity", "SeriousAdverseEvent"]
        }
        """;

    public static async Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        // EntityType: "Device" explicit -- default would normalize to
        // "deviceonboarded" (this Name's own default), which happens to
        // be the only event type that ever patches a device record in
        // this workflow, but matching the feature doc's own literal
        // "trial1:Device:dev-0091" EntityId keeps the sample's own data
        // legible against that doc's diagrams.
        await registry.RegisterAsync("DeviceOnboarded", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: DeviceOnboardedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.DeviceId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Device"), ct);

        await registry.RegisterAsync("AdverseEventReported", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: AdverseEventReportedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.AeId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "AdverseEvent"), ct);

        await VitalsSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "review:ae", ct);
    }
}
