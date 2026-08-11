namespace EventStore.Domain.EntityStore;

// Shape is the data-model authority: docs/data/entity-store.md. Built to
// that doc's full documented column set now (ShardKey/AuthorityStatus/
// LastAppliedOriginId included) even though "Entity-Centric Core Rebuild"
// only actively sets a subset of them -- the same "avoid a second wave of
// migrations" precedent StoredEvent itself already established in
// "Scaffolding & Persistence." ShardKey (ADR-034, "Sharding & Replication")
// and LastAppliedOriginId (ADR-033, multi-site) stay null until those later
// items populate them; AuthorityStatus defaults "accepted" (matching
// StoredEvent's own existing default) until "Non-Authoritative Capture"
// (ADR-035/042) starts actually gating on it.
public class EntityStoreRow
{
    public string EntityId { get; set; } = default!;  // {appId}:{entityType}:{uniqueId}, PK
    public string EntityType { get; set; } = default!; // denormalized for query/shard routing (ADR-034)
    public string? ShardKey { get; set; }
    public long Version { get; set; }                  // DATA-CHANGE counter -- only bumps when Data actually changes (ADR-029)
    public string Data { get; set; } = default!;        // current materialized snapshot (typed properties)
    public string Extensions { get; set; } = default!;  // overflow bag for properties not in the current known schema (ADR-022)
    public string Hash { get; set; } = default!;        // SHA-256 of canonicalized Data (ADR-019's hash primitive, a per-entity application)
    public int SchemaVersion { get; set; }               // current shape, post-upcast (best effort -- ADR-018)
    public string AuthorityStatus { get; set; } = "accepted"; // rolled up from contributing events -- advisory (ADR-035)
    public long LastAppliedSequenceNumber { get; set; }  // REPLAY CHECKPOINT -- always advances, including past a rejected late arrival (ADR-029)
    public DateTimeOffset LastAppliedLogicalTime { get; set; } // high-water mark for fold ordering, compared against OccurredAt (ADR-029)
    public bool LateArrivalFlag { get; set; }             // rolled up from contributing events (ADR-029)
    public string? LastAppliedOriginId { get; set; }      // which site/peer's event this row last folded (ADR-033)
    public DateTimeOffset UpdatedAt { get; set; }
}
