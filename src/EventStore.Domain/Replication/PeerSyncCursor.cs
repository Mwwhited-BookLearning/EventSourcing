namespace EventStore.Domain.Replication;

// Shape is the data-model authority: docs/data/schema-registry.md, "Peer-
// sync cursor (ADR-033)". The durable, per-peer resumption point after a
// restart -- sync picks up exactly where it left off, the same "durable
// checkpoint, not memory" discipline ADR-015's ProjectionCheckpoint
// already established for a different consumer. PeerId is the same value
// as that peer's own OriginId (docs/features/replication-and-sharding.md's
// own ER diagram: "LastAppliedOriginId = PeerId -- logical only").
public class PeerSyncCursor
{
    public string PeerId { get; set; } = default!;
    public long LastReceivedSequenceNumber { get; set; }
    public long LastAckedSequenceNumber { get; set; }
    public DateTimeOffset LastSyncAttemptAt { get; set; }
    public DateTimeOffset? LastSyncSuccessAt { get; set; }
}
