using EventStore.SchemaRegistry;

namespace Samples.Vitals;

// Workflow C -- Trial Data Export & Subject Rights
// (docs/domains/clinical-trials-device-telemetry/features/trial-data-
// export-and-subject-rights.md). EntityErasureRequested itself needs no
// registration here -- EventStore.Erasure's own EntityErasureRequestedEventType
// registers it lazily, the first time ErasureKeyService.GetOrCreateAsync
// runs for this AppId, already carrying the real RequiredClaims
// "erasure:request" gate the feature doc's own Gherkin expects.
public static class VitalsWorkflowC
{
    public const string AppId = VitalsWorkflowA.AppId;

    private const string ConsentWithdrawnSchema = """
        {
          "type": "object",
          "properties": {
            "SubjectId": { "type": "string" },
            "WithdrawnAt": { "type": "string" },
            "Reason": { "type": "string" }
          },
          "required": ["SubjectId", "WithdrawnAt", "Reason"]
        }
        """;

    public static Task RegisterAsync(SchemaRegistryService registry, CancellationToken ct = default) =>
        registry.RegisterAsync("ConsentWithdrawn", new RegisterEventTypeRequest(
            AppId: AppId, JsonSchema: ConsentWithdrawnSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.SubjectId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null, EntityType: "Patient"), ct);
}
