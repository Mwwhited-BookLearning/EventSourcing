using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Interchange.Abstractions;

namespace EventStore.Interchange;

// ADR-072 -- FHIR is RESTful/JSON-native, needs no MLLP-style bridge: an
// ordinary HTTP body this adapter parses directly. Handles the FHIR
// Patient resource specifically (verified against the real FHIR R4
// Patient shape: resourceType/id/name[].family/name[].given[]) -- this
// build stage's own scoped subset, not the full FHIR resource catalog.
public class FhirAdapter : IInterchangeFormatAdapter
{
    public Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default)
    {
        var resource = JsonNode.Parse(rawMessage) as JsonObject ?? throw new FormatException("invalid FHIR JSON resource");
        var resourceType = resource["resourceType"]?.GetValue<string>() ?? throw new FormatException("missing resourceType");
        if (resourceType != "Patient")
            throw new NotSupportedException($"FhirAdapter only handles the Patient resource in this build stage, got: {resourceType}");

        var patientId = resource["id"]?.GetValue<string>() ?? throw new FormatException("missing Patient.id");
        var nameEntry = (resource["name"] as JsonArray)?.FirstOrDefault() as JsonObject;
        var lastName = nameEntry?["family"]?.GetValue<string>() ?? "";
        var firstName = (nameEntry?["given"] as JsonArray)?.FirstOrDefault()?.GetValue<string>() ?? "";

        var payload = JsonSerializer.Serialize(new { PatientId = patientId, LastName = lastName, FirstName = firstName });
        return Task.FromResult(new InterchangeInboundResult("PatientAdmitted", payload, ReviewPending: true));
    }

    public Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default) =>
        throw new NotSupportedException("FhirAdapter is inbound-only in this build stage -- no scoped outbound FHIR use case here");
}
