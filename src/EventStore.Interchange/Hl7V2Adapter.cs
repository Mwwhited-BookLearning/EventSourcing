using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Interchange.Abstractions;

namespace EventStore.Interchange;

// ADR-072 -- real HL7v2 pipe-delimited segment parsing (verified against
// the actual v2.3 message structure, not approximated): segments are
// carriage-return-separated, fields within a segment pipe-separated,
// subcomponents caret-separated. MSH's own field numbering is offset by
// one versus every other segment (MSH-1 IS the field separator character
// itself), a real quirk of the standard, not a bug here. Handles ADT^A01
// (admit) specifically -- this build stage's own scoped subset, not the
// full v2.x message catalog.
public class Hl7V2Adapter : IInterchangeFormatAdapter
{
    public Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default)
    {
        var segments = rawMessage.Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var msh = segments.FirstOrDefault(s => s.StartsWith("MSH|", StringComparison.Ordinal))
            ?? throw new FormatException("missing required MSH segment");
        var mshFields = msh.Split('|');
        // MSH-9 is Message Type (e.g. "ADT^A01") -- index 8 here since
        // MSH-1 (the pipe itself) isn't a split-produced element the way
        // every other segment's own field 1 is.
        var messageType = mshFields.Length > 8 ? mshFields[8] : throw new FormatException("MSH-9 (message type) is required");
        if (!messageType.StartsWith("ADT^A01", StringComparison.Ordinal))
            throw new NotSupportedException($"Hl7V2Adapter only handles ADT^A01 in this build stage, got: {messageType}");

        var pid = segments.FirstOrDefault(s => s.StartsWith("PID|", StringComparison.Ordinal))
            ?? throw new FormatException("missing required PID segment for ADT^A01");
        var pidFields = pid.Split('|');
        var patientId = pidFields.Length > 3 && pidFields[3].Length > 0
            ? pidFields[3].Split('^')[0]
            : throw new FormatException("PID-3 (patient identifier list) is required");

        var nameParts = pidFields.Length > 5 ? pidFields[5].Split('^') : [];
        var lastName = nameParts.Length > 0 ? nameParts[0] : "";
        var firstName = nameParts.Length > 1 ? nameParts[1] : "";

        var payload = JsonSerializer.Serialize(new { PatientId = patientId, LastName = lastName, FirstName = firstName });
        // ADR-035/072 -- inbound EMR-sourced data defaults to non-
        // authoritative capture (ReviewPending), never accepted outright.
        return Task.FromResult(new InterchangeInboundResult("PatientAdmitted", payload, ReviewPending: true));
    }

    public Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default) =>
        throw new NotSupportedException("Hl7V2Adapter is inbound-only in this build stage -- HL7v2 has no scoped outbound use case here");
}
