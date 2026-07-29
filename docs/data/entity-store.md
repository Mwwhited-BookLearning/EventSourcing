[← Data model index](../02-data-model.md)

# Entity Store

Mutable, versioned, hashed — folded from `StoredEvent` (`event-log.md`)
by the same always-on projection mechanism `ADR-015` built for opt-in
CQRS projections, except this one runs for every entity automatically
(`ADR-021`). This is the read path for "current state of X" — see
`patterns/cqrs-and-materialized-views.md` for the general pattern this
table is an instance of.

```csharp
public class EntityStoreRow
{
    public string EntityId { get; set; } = default!;    // {appId}:{entityType}:{uniqueId}, PK
    public string EntityType { get; set; } = default!;  // denormalized for query/shard routing (queued sharding ADR)
    public string? ShardKey { get; set; }                // computed from EntityId/EntityType (queued sharding ADR)
    public long Version { get; set; }                    // DATA-CHANGE counter — only bumps when Data actually changes (ADR-029); distinct from LastAppliedSequenceNumber below
    public string Data { get; set; } = default!;         // current materialized snapshot (typed properties)
    public string Extensions { get; set; } = default!;   // overflow bag for properties not in the current known schema (ADR-022)
    public string Hash { get; set; } = default!;         // SHA-256 of canonicalized Data — per-entity integrity/diff, a different
                                                           // application of ADR-019's hash primitive than the event ChainHash
    public int SchemaVersion { get; set; }                // current shape, post-upcast (best effort — ADR-018)
    public string AuthorityStatus { get; set; } = default!; // rolled up from contributing events — advisory (queued non-authoritative-capture ADR)
    public long LastAppliedSequenceNumber { get; set; }   // REPLAY CHECKPOINT — always advances past every event processed, including a rejected late arrival (ADR-029); distinct from Version above
    public DateTimeOffset LastAppliedLogicalTime { get; set; } // high-water mark for fold ordering — compared against OccurredAt, not SequenceNumber (ADR-029)
    public bool LateArrivalFlag { get; set; }             // rolled up from contributing events (ADR-029)
    public string? LastAppliedOriginId { get; set; }      // origin of the most recent fold (queued replication ADR)
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## Why `Version` and `LastAppliedSequenceNumber` are two different columns

`LastAppliedSequenceNumber` tracks replay progress through the event
log — it always advances past every event the fold step has looked at,
including one whose effect was rejected (a late arrival, `ADR-029`),
because otherwise the fold would reconsider that same rejected event
forever. `Version` tracks whether the *materialized data itself* has
changed — it only increments when `Data` actually gets a new value.
A late arrival that gets flagged and skipped moves the checkpoint forward
without touching `Version`, since nothing about the entity's visible
state changed. See `ADR-029` for the full reasoning and `ADR-024` for the
conflict-detection mechanism `Version` also supports (`ExpectedVersion`
on publish, compared against this column at fold time).

## Fold ordering (`ADR-029`)

`LastAppliedLogicalTime` is compared against each incoming event's
`OccurredAt` (`event-log.md`) — not `SequenceNumber` — specifically so a
late-arriving event that logically happened *before* something already
folded can't silently overwrite newer data with stale values. See
`ADR-029` and `patterns/README.md`'s watermarks/event-time entry for the
general pattern this implements.

## Read-side data model (CQRS projections) is elsewhere, deliberately

`ChangeKind` (`schema-registry.md`) is the one write-side model addition
the read side needs — everything else a custom projection needs
(`ProjectionCheckpoint`, `ProjectionSnapshot`, and each projection's own
read-model tables, e.g. `OrderSummary`) lives in a **separate**
`ProjectionsDbContext`, in a separate database, owned by
`EventStore.Projections.Host`, not this `EventStoreContext`. See
`../09-cqrs-read-models.md` and `ADR-015`/`ADR-016` for the full design —
deliberately not duplicated here, since keeping the write-side model from
growing read-side entities is itself part of demonstrating the CQRS split
this project exists to show.
