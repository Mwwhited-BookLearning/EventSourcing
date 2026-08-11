namespace EventStore.Domain.Streaming;

// ADR-081 -- StoredEvent.TelemetryPointer (docs/data/event-log.md) is a
// JSON-serialized List<TelemetryPointerEntry>, generalized from a single
// object so a detection spanning a correlated pattern across several
// channels at once can name every contributing channel's window in one
// event. An ordinary single-channel detection is simply a one-entry list,
// not a different shape.
public record TelemetryPointerEntry(string ChannelId, string? ThreadId, DateTimeOffset FromTimestamp, DateTimeOffset? ToTimestamp);
