using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EventStore.Interchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Bulk Ingestion & External Interchange-Format Adapters" (docs/08-build-
// plan.md, ADR-072) -- pure adapter-transform scenarios, no db/HTTP
// needed: real HL7v2 pipe-delimited parsing, real FHIR JSON parsing, and
// the two outbound XML transforms (verified against the real ICH/GS1
// element names before writing them, per this project's own "verify
// before citing" rule). The MLLP wire protocol itself and the batch/FHIR
// HTTP endpoints are covered separately, where a real socket/HTTP round
// trip is actually needed.
[TestClass]
public class InterchangeAdapterTests
{
    [TestMethod]
    public async Task Hl7V2AdapterParsesAnAdtA01MessageIntoAPatientAdmittedRequestStartingBelowAccepted()
    {
        var adapter = new Hl7V2Adapter();
        var message = "MSH|^~\\&|SendingApp|SendingFac|ReceivingApp|ReceivingFac|20260810120000||ADT^A01|MSG00001|P|2.3\r" +
                      "EVN|A01|20260810120000\r" +
                      "PID|1||123456^^^MRN||DOE^JOHN||19800101|M\r";

        var result = await adapter.ParseInboundAsync("hl7v2-demo", message);

        Assert.AreEqual("PatientAdmitted", result.EventType);
        Assert.IsTrue(result.ReviewPending, "non-authoritative capture is the default for EMR-sourced data (ADR-035/072)");
        var payload = JsonNode.Parse(result.Payload)!;
        Assert.AreEqual("123456", payload["PatientId"]!.GetValue<string>());
        Assert.AreEqual("DOE", payload["LastName"]!.GetValue<string>());
        Assert.AreEqual("JOHN", payload["FirstName"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Hl7V2AdapterRejectsAMessageTypeItDoesNotHandleInThisBuildStage()
    {
        var adapter = new Hl7V2Adapter();
        var message = "MSH|^~\\&|SendingApp|SendingFac|ReceivingApp|ReceivingFac|20260810120000||ORU^R01|MSG00002|P|2.3\r";

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => adapter.ParseInboundAsync("hl7v2-demo", message));
    }

    [TestMethod]
    public async Task FhirAdapterParsesAPatientResourceOverOrdinaryHttpsWithNoMllpOrTcpInvolved()
    {
        var adapter = new FhirAdapter();
        var resource = """{ "resourceType": "Patient", "id": "pat-42", "name": [{ "family": "Smith", "given": ["Jane"] }] }""";

        var result = await adapter.ParseInboundAsync("fhir-demo", resource);

        Assert.AreEqual("PatientAdmitted", result.EventType);
        Assert.IsTrue(result.ReviewPending);
        var payload = JsonNode.Parse(result.Payload)!;
        Assert.AreEqual("pat-42", payload["PatientId"]!.GetValue<string>());
        Assert.AreEqual("Smith", payload["LastName"]!.GetValue<string>());
        Assert.AreEqual("Jane", payload["FirstName"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task IchE2bR3AdapterTransformsAMatchingEventIntoARealE2bR3ShapedIcsrXmlImmediatelyBeforeDelivery()
    {
        var adapter = new IchE2bR3Adapter();
        var payload = JsonNode.Parse("""{ "CaseId": "case-1", "PatientId": "pat-1", "DrugName": "Aspirin", "ReactionTerm": "Headache" }""");

        var xml = await adapter.FormatOutboundAsync("pharma-demo", "AdverseEventReported", payload);

        var document = XDocument.Parse(xml);
        var hl7V3 = (XNamespace)"urn:hl7-org:v3";
        Assert.AreEqual(hl7V3 + "MCCI_IN200100UV01", document.Root!.Name, "the real ICH E2B(R3) batch envelope root element/namespace, verified before writing this");
        Assert.AreEqual("case-1", document.Descendants(hl7V3 + "safetyreportid").Single().Value);
        Assert.AreEqual("Aspirin", document.Descendants(hl7V3 + "medicinalproduct").Single().Value);
        Assert.AreEqual("Headache", document.Descendants(hl7V3 + "reactionmeddrapt").Single().Value);
    }

    [TestMethod]
    public async Task Gs1EpcisAdapterTransformsAMatchingEventIntoARealEpcisObjectEvent()
    {
        var adapter = new Gs1EpcisAdapter();
        var payload = JsonNode.Parse("""{ "Epc": "urn:epc:id:sgtin:4012345.011111.9876", "BizStep": "urn:epcglobal:cbv:bizstep:shipping", "ReadPoint": "urn:epc:id:sgln:4012345.00005.0" }""");

        var xml = await adapter.FormatOutboundAsync("dscsa-demo", "ShipmentDispatched", payload);

        var document = XDocument.Parse(xml);
        var epcis = (XNamespace)"urn:epcglobal:epcis:xsd:2";
        Assert.AreEqual(epcis + "EPCISDocument", document.Root!.Name, "the real GS1 EPCIS 2.0 root element/namespace, verified before writing this");
        Assert.AreEqual("urn:epc:id:sgtin:4012345.011111.9876", document.Descendants("epc").Single().Value);
        Assert.AreEqual("OBSERVE", document.Descendants("action").Single().Value);
        Assert.AreEqual("urn:epcglobal:cbv:bizstep:shipping", document.Descendants("bizStep").Single().Value);
    }

    [TestMethod]
    public async Task OutboundOnlyAdaptersRejectAnInboundCallAndInboundOnlyAdaptersRejectAnOutboundCall()
    {
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => new IchE2bR3Adapter().ParseInboundAsync("appId", "irrelevant"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => new Gs1EpcisAdapter().ParseInboundAsync("appId", "irrelevant"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => new Hl7V2Adapter().FormatOutboundAsync("appId", "eventType", null));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => new FhirAdapter().FormatOutboundAsync("appId", "eventType", null));
    }
}
