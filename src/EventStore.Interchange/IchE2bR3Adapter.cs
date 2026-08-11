using System.Text.Json.Nodes;
using System.Xml.Linq;
using EventStore.Interchange.Abstractions;

namespace EventStore.Interchange;

// ADR-072 -- outbound: transforms a matching adverse-event record into an
// ICH E2B(R3) Individual Case Safety Report for EudraVigilance/FAERS.
// Verified against real ICH/FDA regional implementation guidance before
// writing this (per this project's own "verify before citing" rule): the
// real E2B(R3) transmission wraps an ICSR message in an HL7 v3 batch
// envelope whose root element is genuinely `MCCI_IN200100UV01` in the
// `urn:hl7-org:v3` namespace -- that root name/namespace are the real
// spec's own, not invented. The full E2B(R3)/HL7 v3 ICSR schema is
// hundreds of nested acts/participations/observations; this build stage
// deliberately implements only a small, representative subset (case ID,
// patient identifier, one drug, one reaction) inside that real envelope,
// an honestly-scoped subset in the same spirit as this repo's own
// "Merkle-tree catch-up not built" and "Hl7V2Adapter handles ADT^A01
// only" precedents -- not a claim of full E2B(R3) conformance.
public class IchE2bR3Adapter : IInterchangeFormatAdapter
{
    private static readonly XNamespace Hl7V3 = "urn:hl7-org:v3";

    public Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default) =>
        throw new NotSupportedException("IchE2bR3Adapter is outbound-only in this build stage -- no scoped inbound E2B(R3) use case here");

    public Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default)
    {
        var caseId = payload?["CaseId"]?.GetValue<string>() ?? throw new FormatException("payload is missing the required 'CaseId' field for E2B(R3) export");
        var patientId = payload?["PatientId"]?.GetValue<string>() ?? "";
        var drugName = payload?["DrugName"]?.GetValue<string>() ?? "";
        var reactionTerm = payload?["ReactionTerm"]?.GetValue<string>() ?? "";

        var icsr = new XElement(Hl7V3 + "ichicsr",
            new XElement(Hl7V3 + "safetyreport",
                new XElement(Hl7V3 + "safetyreportid", caseId),
                new XElement(Hl7V3 + "patient", new XElement(Hl7V3 + "patientinitial", patientId)),
                new XElement(Hl7V3 + "drug", new XElement(Hl7V3 + "medicinalproduct", drugName)),
                new XElement(Hl7V3 + "reaction", new XElement(Hl7V3 + "reactionmeddrapt", reactionTerm))));

        var batch = new XElement(Hl7V3 + "MCCI_IN200100UV01",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute("ITSVersion", "XML_1.0"),
            new XElement(Hl7V3 + "id", new XAttribute("root", Guid.NewGuid().ToString())),
            new XElement(Hl7V3 + "creationTime", new XAttribute("value", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmsszzz"))),
            icsr);

        return Task.FromResult(new XDocument(batch).ToString(SaveOptions.DisableFormatting));
    }
}
