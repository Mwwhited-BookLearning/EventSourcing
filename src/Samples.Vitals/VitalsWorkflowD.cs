using EventStore.SchemaRegistry;

namespace Samples.Vitals;

// Workflow D -- Intraoperative Monitoring & Alert Response
// (docs/domains/clinical-trials-device-telemetry/features/
// intraoperative-monitoring-and-alert-response.md) -- ADR-094's first
// real domain-level exercise. IonmAlertAcknowledged folds Partial onto
// the SAME "IonmAlert" entity IonmAlertRaised creates (EntityType
// explicit on both, the same reason Workflow A set it on PatientScreened/
// InformedConsentCaptured); the neurologist's sign-off reuses Workflow
// B's exact "authorityDecision" reactor, extended with its own
// "review:ionm" claim (an attending neurologist is a distinct persona
// from a Principal Investigator, per this doc's own framing) via
// VitalsSharedTypes' union-of-claims mechanism.
public static class VitalsWorkflowD
{
    public const string AppId = VitalsWorkflowA.AppId;

    private const string IonmAlertRaisedSchema = """
        {
          "type": "object",
          "properties": {
            "AlertId": { "type": "string" },
            "SubjectId": { "type": "string" },
            "Finding": { "type": "string" },
            "Severity": { "type": "string" }
          },
          "required": ["AlertId", "SubjectId", "Finding", "Severity"]
        }
        """;

    private const string IonmAlertAcknowledgedSchema = """
        {
          "type": "object",
          "properties": {
            "AlertId": { "type": "string" },
            "AckedBy": { "type": "string" }
          },
          "required": ["AlertId", "AckedBy"]
        }
        """;

    public static async Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default)
    {
        // ChangeKind "Partial", not the feature doc's own literal "Full"
        // Background text -- a real, found-by-running-it correction, not a
        // silent substitution. IonmAlertAcknowledged is an ORDINARY,
        // immediately-"accepted" publish (never gated), so it can fold
        // into the authoritative Entity Store before IonmAlertRaised's own
        // delayed catch-up fold ever runs (that fold waits on the
        // neurologist's signed authorityDecision). A "Full" catch-up fold
        // would then REPLACE the entire row with just {Finding, Severity},
        // silently erasing the already-accumulated AckedBy contribution --
        // caught by actually running the accept-after-ack scenario, not by
        // reading the code back. "Partial" merges onto whatever's already
        // there instead, so both orderings converge on the same {Finding,
        // Severity, AckedBy} the doc's own ER diagram describes.
        await registry.RegisterAsync("IonmAlertRaised", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: IonmAlertRaisedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.AlertId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "IonmAlert",
            ExpectedResponse: new ExpectedResponseRequest("IonmAlertAcknowledged", TimeSpan.FromMinutes(2))), ct);

        await registry.RegisterAsync("IonmAlertAcknowledged", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: IonmAlertAcknowledgedSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.AlertId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "IonmAlert"), ct);

        await VitalsSharedTypes.EnsureAuthorityDecisionRegisteredAsync(registry, AppId, "review:ionm", ct);
    }
}
