namespace EventStore.Replication;

// The wire shape one peer pushes to another -- enough fields to append a
// faithful copy of the original StoredEvent locally, exactly as if it had
// arrived through this site's own client Inbox (ADR-033). SequenceNumber
// here is the SENDING site's own local numbering for this event -- never
// stored on the receiving side's StoredEvent row (which gets its own,
// unrelated local SequenceNumber on insert) -- carried purely so the
// receiver can advance its own PeerSyncCursor[fromPeerId].
// LastReceivedSequenceNumber bookkeeping.
public record ReplicatedEventPayload(
    long SequenceNumber,
    Guid EventId,
    string AppId,
    string EventType,
    int SchemaVersion,
    string Payload,
    string PayloadHash,
    DateTimeOffset OccurredAt,
    string ActorId,
    string OriginId,
    string LogicalClock,
    long? ExpectedVersion,
    List<Guid>? ParentEventIds,
    // ADR-035/042 -- a faithful replicated copy must preserve the
    // ORIGINATING site's own trust axis verbatim (e.g. an "unattested"
    // reading stays unattested at the receiving site too), never silently
    // reset to StoredEvent's own "accepted" default the way an omitted
    // field would. Found and fixed while building "Non-Authoritative
    // Capture" -- absent from this record when "Sharding & Replication"
    // first shipped it, since no event carried a non-default AuthorityStatus
    // at that point.
    string AuthorityStatus,
    string? AttestedActorId,
    string? AttestedClaims);

public record PeerSyncPushRequest(string FromPeerId, List<ReplicatedEventPayload> Events, List<KnownPeer> KnownPeers);

public record PeerSyncPushResponse(long AckedThroughSequenceNumber, List<KnownPeer> KnownPeers);

// ADR-051's own "the seed list only ever needs to name a subset of
// currently-live sites" -- every push/ack round trip also exchanges
// what each side currently knows, so a peer configured with only one
// seed eventually learns every other peer transitively.
public record KnownPeer(string PeerId, string Address);
