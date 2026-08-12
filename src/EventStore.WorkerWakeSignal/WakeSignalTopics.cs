namespace EventStore.WorkerWakeSignal;

// Centralizes the topic-name constants for wake signals whose worker-side
// project cannot be referenced directly by PublishService (EventStore.Inbox)
// without creating a circular project reference: EventStore.Derivation,
// EventStore.Replication, and EventStore.ExpectedResponse each already
// depend on EventStore.Inbox (for PublishService itself), so Inbox
// referencing any of them back, just to read their own Topic constant,
// would cycle. Both ends reference this shared, dependency-free project
// instead -- the same "can never drift apart by a typo" guarantee
// RouterWorker.Topic already establishes for its own single-project case,
// extended to the multi-project ones. WebhookOutboxPump/ChannelDerivationWorker
// don't need an entry here -- their own notify call sites (RouterWorker,
// TelemetrySampleWriter) already sit in a project that can reference their
// Topic constant directly with no cycle.
public static class WakeSignalTopics
{
    public const string Derivation = "derivation";
    public const string ExpectedResponse = "expectedresponse";
    public const string PeerSync = "peersync";
}
