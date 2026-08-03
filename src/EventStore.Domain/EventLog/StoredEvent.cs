namespace EventStore.Domain.EventLog;

// Shape is the data-model authority: docs/data/event-log.md. Every field
// below is already load-bearing for a *later* build-plan item, but built
// now in full per "Scaffolding & Persistence"'s own scope, to avoid a
// second wave of migrations once those items start using it.
public class StoredEvent
{
    public long SequenceNumber { get; set; }
    public string? OriginId { get; set; }
    public string? LogicalClock { get; set; }
    public Guid EventId { get; set; }
    public string EntityId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public int SchemaVersion { get; set; }
    public EventKind EventKind { get; set; } = EventKind.Original;
    public Guid? MaterializationOfEventId { get; set; }
    public long? ExpectedVersion { get; set; }
    public string Payload { get; set; } = default!;
    public string PayloadHash { get; set; } = default!;
    public string ChainHash { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string? SchemaStatus { get; set; }
    public bool ConflictFlag { get; set; }
    public bool LateArrivalFlag { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ActorId { get; set; } = default!;
    public string? AttestedActorId { get; set; }
    public string? AttestedClaims { get; set; }
    public string AuthorityStatus { get; set; } = "accepted";
    public Guid? AuthorityDecisionRef { get; set; }
    public string? TelemetryPointer { get; set; }
    public Signature? Signature { get; set; }
    public long? OriginalSequenceNumber { get; set; }
    public string? OriginalChainHash { get; set; }
    public string? ImportedFrom { get; set; }
    public int DerivationHopCount { get; set; }
}

public enum EventKind
{
    Original,              // every event published today -- subject to normal fold
    UpcastMaterialization  // a persisted upcast result (ADR-027) -- never folded
}
