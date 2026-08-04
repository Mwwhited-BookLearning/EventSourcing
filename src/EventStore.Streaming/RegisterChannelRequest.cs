namespace EventStore.Streaming;

// docs/features/streaming-channels.md's "Registering a new origin channel"
// scenario shape. `AppId` is a temporary, explicit request field, the same
// interim posture every other registration surface in this build stage
// still carries (SchemaRegistryService.RegisterAsync, DerivationRegistrationService).
public record RegisterChannelRequest(
    string AppId,
    string EntityId,
    string ContentKind,
    string? SampleType,
    string? MimeType,
    long? SampleIntervalMicros,
    string Origin,
    string? ThreadId,
    List<string>? SourceChannelIds,
    string? TransformKind,
    string? RequiredReadClaim);
