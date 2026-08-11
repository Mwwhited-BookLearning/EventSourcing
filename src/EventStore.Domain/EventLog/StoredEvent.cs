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
    public string AppId { get; set; } = default!;
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
    // ADR-088 -- server-assigned, set once by EventAppender.AppendAsync at
    // the same moment SequenceNumber becomes known (never client-supplied,
    // never revised). Distinct from OccurredAt (client-declared LOGICAL
    // occurrence time, fold-order-bearing) -- this is wall-clock ARRIVAL
    // time at this site, the timestamp Router fold-lag instrumentation
    // diffs against, the same distinction ADR-029 already draws for
    // OccurredAt itself.
    public DateTimeOffset AppendedAt { get; set; }
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
    // ADR-094 -- Correlation Identifier (Hohpe & Woolf): the EventId this
    // event is a reply to. Optional on any publish, never existence-
    // validated (unlike parentEventIds' own Strict/Permissive fork) -- a
    // value naming an EventId that doesn't (yet, or ever) exist is simply a
    // response that correlates to nothing findable, not a rejected publish.
    // The eighth distinct relationship-shaped envelope field (CLAUDE.md).
    public Guid? RespondsToEventId { get; set; }
}

public enum EventKind
{
    Original,              // every event published today -- subject to normal fold
    UpcastMaterialization  // a persisted upcast result (ADR-027) -- never folded
}

// ADR-089 -- left behind in the primary table when a contiguous segment of
// StoredEvent (or, independently, AccessLogEntry) rows is detached/archived
// to an externalized IAttachmentContentStore backend. Ongoing live chain
// verification for events appended AFTER the archived segment needs only
// this row's own ChainHashAtRangeEnd -- it never touches archived data. The
// SAME shape is reused for both hash-chained stores (docs/data/access-
// log.md's own "one mechanism, applied to both" framing) -- as two
// genuinely separate EF Core shared-type-entity tables
// (EventStoreContext.EventLogChainCheckpoints/AccessLogChainCheckpoints),
// never one shared table, so the two stores' own checkpoints can never
// collide. Id is a surrogate key (no natural composite key exists once
// more than one archival operation has happened -- SequenceNumberRangeStart
// alone isn't guaranteed unique across the two shared-type tables), the
// same convention AttachmentRef's own Id already established.
public class ChainCheckpoint
{
    public int Id { get; set; }
    public long SequenceNumberRangeStart { get; set; }
    public long SequenceNumberRangeEnd { get; set; }
    public string ChainHashAtRangeEnd { get; set; } = default!;
    public string ContentProviderKey { get; set; } = default!; // which registered IAttachmentContentStore backend holds the archived segment (ADR-032)
    public string ContentProviderRef { get; set; } = default!; // opaque, provider-specific locator for the segment's NDJSON blob
}
