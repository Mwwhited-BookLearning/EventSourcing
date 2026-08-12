[← Data model index](../02-data-model.md)

# Entity Store

Mutable, versioned, hashed — folded from `StoredEvent` (`event-log.md`)
by the same always-on projection mechanism `ADR-015` built for opt-in
CQRS projections, except this one runs for every entity automatically
(`ADR-021`). This is the read path for "current state of X" — see
`patterns/cqrs-and-materialized-views.md` for the general pattern this
table is an instance of.

**This is the *authoritative* view, gated on `AuthorityStatus` (`ADR-042`)**:
an event only folds here once `AuthorityStatus` reaches `accepted` — an
`unattested`/`pending_review` event is fully persisted in the Event Log
but does not yet update this table. See `LiveEntityStoreRow` below for
the ungated, "everything including not-yet-approved data" counterpart.

```csharp
public class EntityStoreRow
{
    public string EntityId { get; set; } = default!;    // {appId}:{entityType}:{uniqueId}, PK
    public string EntityType { get; set; } = default!;  // denormalized for query/shard routing (ADR-034)
    public string? ShardKey { get; set; }                // computed from EntityId/EntityType (ADR-034)
    public long Version { get; set; }                    // DATA-CHANGE counter — only bumps when Data actually changes (ADR-029); distinct from LastAppliedSequenceNumber below
    public string Data { get; set; } = default!;         // current materialized snapshot (typed properties)
    public string Extensions { get; set; } = default!;   // overflow bag for properties not in the current known schema (ADR-022)
    public string PropertyVersions { get; set; } = "{}"; // property name -> the Version at which ITS OWN value last actually differed — ADR-024's per-property conflict comparison; see "Why PropertyVersions exists" below
    public string Hash { get; set; } = default!;         // SHA-256 of canonicalized Data — per-entity integrity/diff, a different
                                                           // application of ADR-019's hash primitive than the event ChainHash
    public int SchemaVersion { get; set; }                // current shape, post-upcast (best effort — ADR-018)
    public string AuthorityStatus { get; set; } = "accepted"; // rolled up from contributing events — advisory (ADR-035)
    public long LastAppliedSequenceNumber { get; set; }   // REPLAY CHECKPOINT — always advances past every event processed, including a rejected late arrival (ADR-029); distinct from Version above
    public DateTimeOffset LastAppliedLogicalTime { get; set; } // high-water mark for fold ordering — compared against OccurredAt, not SequenceNumber (ADR-029)
    public bool LateArrivalFlag { get; set; }             // rolled up from contributing events (ADR-029)
    public string? LastAppliedOriginId { get; set; }      // which site/peer's event this Entity Store row last folded (ADR-033) -- same "Origin" terminology collision as StoredEvent.OriginId; see that field's note in docs/data/event-log.md, not related to TelemetryChannel.Origin
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## Live View — the ungated counterpart (`ADR-042`)

A second, framework-level, always-on materialized view, folded by the
exact same mechanism as `EntityStoreRow` above, minus the
`AuthorityStatus` gate — every event updates this the moment it's
received, `unattested`/`pending_review`/`rejected` included. This is
what a "live monitoring" consumer reads when seeing not-yet-approved
data immediately is more valuable than waiting for review.

```csharp
public class LiveEntityStoreRow
{
    public string EntityId { get; set; } = default!;    // same {appId}:{entityType}:{uniqueId} key as EntityStoreRow, PK
    public string EntityType { get; set; } = default!;
    public string Data { get; set; } = default!;         // folds every event immediately, no AuthorityStatus gate
    public string Extensions { get; set; } = default!;
    public string AuthorityStatus { get; set; } = default!; // the MOST RECENT contributing event's status -- unattested/pending_review/accepted/rejected, never rolled up/hidden
    public long LastAppliedSequenceNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

**Every read of this table is wrapped with `isAuthoritative: false` at
the query surface** — a whole-row/whole-view marker, not a per-field one
(contrast `ADR-009`'s masking wrapper, which redacts individual field
*values* — a deliberately different granularity, not the same
mechanism reused). There is no query mode that silently omits this
marker; a caller reading `LiveEntityStoreRow` always sees it labeled.
`EntityStoreRow` itself never carries this marker at all — only
`LiveEntityStoreRow` does, so there's no ambiguous case where a caller
has to check a flag to know which view they're looking at.

A rejected event's contribution stays visible here too (never deleted,
`README.md`'s governing principle) — re-labeled `rejected` once the
decision lands, not removed.

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

## Why `PropertyVersions` exists (`ADR-024`)

`Version` alone answers "has the entity changed since some prior
version," but `ADR-024`'s actual Decision is narrower: "two patches based
on the same version touching *different* properties both fold cleanly
regardless of arrival order — that is not a conflict." Answering that
needs to know, per property, when it last actually changed — `Version`
by itself can't distinguish "someone else changed the property I'm also
touching" from "someone else changed some other property entirely."
`PropertyVersions` is that per-property answer: a plain
`{ "propertyName": versionNumber }` map, updated at fold time alongside
`Version` — but only for a property whose *own value* genuinely differs
from before, never merely because it was present in the incoming
payload. That distinction matters concretely: every event re-declares
the entity's own identifying field (e.g. an `OrderId` an `EntityIdField`
resolves against) alongside whatever it actually changes, and that
field's value never differs patch to patch — bumping its entry on every
fold regardless would make it look permanently "just changed" and
manufacture a false conflict for the next, genuinely unrelated patch
that happens to also touch it (found by running
`TwoPatchesBasedOnTheSameVersionTouchingDifferentPropertiesBothFoldCleanlyWithNoConflict`,
not assumed). `RebuildEntityFromAcceptedEventsAsync` (the targeted-
rebuild mechanism, `ADR-035`) recomputes `PropertyVersions` from scratch
alongside `Data`/`Version`, for the same reason it recomputes those: a
stale per-property marker from before a rebuild must never survive it.

## Fold ordering (`ADR-029`)

`LastAppliedLogicalTime` is compared against each incoming event's
`OccurredAt` (`event-log.md`) — not `SequenceNumber` — specifically so a
late-arriving event that logically happened *before* something already
folded can't silently overwrite newer data with stale values. See
`ADR-029` and `patterns/README.md`'s watermarks/event-time entry for the
general pattern this implements.

## Erasure key store (`ADR-057`)

Wrapped-key *metadata* only — the actual key material lives exclusively
in whichever `IErasureKeyStore` backend a deployment configures (Azure
Key Vault, AWS KMS, HashiCorp Vault, ...), never in this table:

```csharp
public class EntityErasureKey
{
    public string EntityId { get; set; } = default!;      // PK -- {appId}:{entityType}:{uniqueId}, same key as EntityStoreRow
    public string KeyReference { get; set; } = default!;   // opaque handle into the configured IErasureKeyStore -- never the key itself
    public string BackendName { get; set; } = default!;    // which IErasureKeyStore this KeyReference resolves against -- see below
    public DateTimeOffset CreatedAt { get; set; }           // first time a classified field was published for this entity
    public DateTimeOffset? ErasedAt { get; set; }            // set once EntityErasureRequested is processed and the key is destroyed
}
```

`BackendName` was added during implementation, correcting this doc's
original shape: backend selection (`ErasureOptions.BackendByAppId`) is
ordinary, changeable-over-time configuration, but decrypt must always
reach the SAME backend that originally created a given key, regardless
of what that `AppId`'s configuration says now — recording it once, at
creation time, on this row is what makes that possible without a global
"never change an `AppId`'s backend" rule.

`ErasedAt` being set is a local convenience flag for "don't bother
calling the key store, it's already gone" — the actual source of truth
for whether the key is destroyed is the `IErasureKeyStore` backend
itself (its own audit trail satisfies `ADR-057`'s "destruction is
auditable" requirement), not this row. This table is, itself, a critical
authoritative store per `ADR-056`'s data-lifecycle classification —
losing it (independent of the external key store) loses the mapping
needed to ever *request* an entity's erasure, though not the ability to
erase, since `KeyReference` is recoverable from the external store's own
listing if truly lost.

### Local backend key material

The "Local" `IErasureKeyStore` backend (dev/single-node deployments with
no external KMS) stores its actual key material in the same
`EventStoreContext`, deliberately durable rather than in-memory (losing
it is equivalent to erasing every subject it protects at once, the same
criticality `EntityErasureKey` itself carries above):

```csharp
public class LocalErasureKeyMaterial
{
    public string KeyReference { get; set; } = default!;  // PK -- "local:{guid}", matches EntityErasureKey.KeyReference
    public byte[]? WrappedKey { get; set; }                 // AES-256 key bytes; set to null on destroy, not merely flagged
    public bool Destroyed { get; set; }
}
```

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
