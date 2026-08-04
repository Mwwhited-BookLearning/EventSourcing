namespace EventStore.Inbox;

// Envelope per docs/features/publish-event.md/03-api-contracts.md.
// `AppId` is a temporary, explicit request field -- same interim posture as
// EventStore.SchemaRegistry.RegisterEventTypeRequest, removed once "Auth +
// Orchestration" resolves it from the caller's scope instead. `Payload` is
// carried as raw JSON text (not a strongly-typed object) since its shape is
// whatever the registered schema for `SchemaVersion` says it is.
// `TelemetryPointer` is ADR-031/081's envelope metadata -- "where in a
// signal did this come from," never Payload -- kept out of the JSON
// Schema validation surface the same way `ParentEventIds` already is.
public record PublishEventRequest(
    string AppId,
    int SchemaVersion,
    string Payload,
    List<Guid>? ParentEventIds,
    Guid? EventId,
    long? ExpectedVersion = null,
    List<Domain.Streaming.TelemetryPointerEntry>? TelemetryPointer = null);
