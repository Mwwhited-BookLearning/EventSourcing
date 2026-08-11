using System.Text.Json.Nodes;
using System.Xml.Linq;
using EventStore.Interchange.Abstractions;

namespace EventStore.Interchange;

// ADR-072 -- outbound: transforms a matching event into a real GS1 EPCIS
// 2.0 ObjectEvent (verified against the actual gs1/EPCIS repository's own
// published XML examples before writing this, per this project's own
// "verify before citing" rule -- root EPCISDocument/EPCISBody/EventList/
// ObjectEvent element names, the epcList/epc/action/bizStep/readPoint
// shape, and the "urn:epcglobal:epcis:xsd:2" namespace are all the real
// spec's own names, not invented). DSCSA trading-partner exchange is this
// build stage's own scoped use case -- one ObjectEvent per event, action
// always "OBSERVE", not the full EPCIS event-type catalog (AggregationEvent/
// TransactionEvent/TransformationEvent) or CBV's full business-step
// vocabulary.
public class Gs1EpcisAdapter : IInterchangeFormatAdapter
{
    private static readonly XNamespace Epcis = "urn:epcglobal:epcis:xsd:2";

    public Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default) =>
        throw new NotSupportedException("Gs1EpcisAdapter is outbound-only in this build stage -- no scoped inbound EPCIS use case here");

    public Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default)
    {
        var epc = payload?["Epc"]?.GetValue<string>() ?? throw new FormatException("payload is missing the required 'Epc' field for GS1 EPCIS export");
        var bizStep = payload?["BizStep"]?.GetValue<string>() ?? "urn:epcglobal:cbv:bizstep:shipping";
        var readPoint = payload?["ReadPoint"]?.GetValue<string>();

        var objectEvent = new XElement("ObjectEvent",
            new XElement("eventTime", DateTimeOffset.UtcNow.ToString("O")),
            new XElement("eventTimeZoneOffset", "+00:00"),
            new XElement("epcList", new XElement("epc", epc)),
            new XElement("action", "OBSERVE"),
            new XElement("bizStep", bizStep),
            readPoint is null ? null : new XElement("readPoint", new XElement("id", readPoint)));

        var document = new XElement(Epcis + "EPCISDocument",
            new XAttribute(XNamespace.Xmlns + "epcis", Epcis.NamespaceName),
            new XAttribute("schemaVersion", "2.0"),
            new XAttribute("creationDate", DateTimeOffset.UtcNow.ToString("O")),
            new XElement("EPCISBody", new XElement("EventList", objectEvent)));

        return Task.FromResult(document.ToString(SaveOptions.DisableFormatting));
    }
}
