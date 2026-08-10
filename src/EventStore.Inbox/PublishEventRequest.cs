using System.Text.Json.Nodes;

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
// `AttachmentContentHashes` is ADR-032's own two-step handoff completed --
// `POST /attachments` returns a hash, this envelope field is where an
// ordinary publish carries it to link a supporting document to the event
// being published, without ever putting the raw bytes in `Payload`.
public record PublishEventRequest(
    string AppId,
    int SchemaVersion,
    string Payload,
    List<Guid>? ParentEventIds,
    Guid? EventId,
    long? ExpectedVersion = null,
    List<Domain.Streaming.TelemetryPointerEntry>? TelemetryPointer = null,
    List<string>? AttachmentContentHashes = null,
    // ADR-035/036/042 -- a self-attested submitter identity and/or its
    // structured capability claims (e.g. a UCAN invocation). Presence of
    // either starts AuthorityStatus at "unattested" rather than the
    // ordinary-publish default "accepted" (ADR-042); credential-agnostic by
    // design (docs/features/non-authoritative-capture.md) -- the shape of
    // AttestedClaims is opaque to the core engine, never schema-validated.
    string? AttestedActorId = null,
    JsonNode? AttestedClaims = null,
    // ADR-042's second trigger -- an automated detector's own "not yet
    // validated" marker, distinct from the identity/self-attestation case
    // above. Starts AuthorityStatus at "pending_review" instead.
    bool ReviewPending = false,
    // ADR-066 -- the signer's stated reason ("reviewed", "approved",
    // "authorship"), required only when the target type has
    // RequiredSignature configured; ignored entirely otherwise, the same
    // "completely unaffected" posture every other optional envelope field
    // here already has.
    string? Meaning = null);
