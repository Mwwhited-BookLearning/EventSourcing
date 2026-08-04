using System.Text.Json.Nodes;

namespace EventStore.Projections.Host;

// Mirrors FollowEndpoints.cs's own SSE envelope shape exactly:
// { eventId, sequenceNumber, occurredAt, parentEventIds, payload }.
public record FollowedEventEnvelope(Guid EventId, long SequenceNumber, DateTimeOffset OccurredAt, List<Guid> ParentEventIds, JsonNode? Payload);
