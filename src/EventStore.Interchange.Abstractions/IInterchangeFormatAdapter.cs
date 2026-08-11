using System.Text.Json.Nodes;

namespace EventStore.Interchange.Abstractions;

// ADR-072 -- a keyed-DI extensibility seam, one implementation per
// external standard (Hl7V2Adapter, FhirAdapter, IchE2bR3Adapter,
// Gs1EpcisAdapter, ...), several active simultaneously. Deliberately
// carries no dependency on EventStore.Inbox's own PublishEventRequest --
// EventStore.Webhooks needs this interface for the OUTBOUND half
// (WebhookOutboxPump's own pre-delivery transform step) and EventStore.
// Router already depends on EventStore.Webhooks (WebhookEnqueueResolver);
// EventStore.Inbox depends on EventStore.Router (EntityIdResolver) --
// so this interface pulling in EventStore.Inbox would reintroduce the
// exact circular project reference "Outbound Webhooks" (item 34) already
// found and fixed once. The concrete inbound adapters (EventStore.
// Interchange, which CAN depend on EventStore.Inbox) convert
// InterchangeInboundResult into a real PublishEventRequest themselves.
//
// Not every adapter supports both directions -- HL7v2/FHIR are inbound-
// only in this build stage, ICH E2B(R3)/GS1 EPCIS are outbound-only. An
// adapter that doesn't support a direction throws NotSupportedException
// from that method, documented on the concrete type, rather than this
// interface being split into two (ADR-072's own text names ONE seam).
public interface IInterchangeFormatAdapter
{
    Task<InterchangeInboundResult> ParseInboundAsync(string appId, string rawMessage, CancellationToken ct = default);

    Task<string> FormatOutboundAsync(string appId, string eventType, JsonNode? payload, CancellationToken ct = default);
}

// EventType is this framework's own registered type name the parsed
// message should publish as; Payload is already-serialized JSON text
// matching that type's registered JsonSchema, ready for the caller to
// wrap into a PublishEventRequest.
public record InterchangeInboundResult(string EventType, string Payload, bool ReviewPending = true);
