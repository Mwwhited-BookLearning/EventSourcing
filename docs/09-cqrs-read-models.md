# CQRS Read-Model Projections

Documents `01`–`08` are the **write side**: an event-sourced store of
record, append-only, queryable only through the general-purpose Publish/
Follow/Lineage/Registry APIs. This document is the **read side**: purpose-
built, query-optimized read models, materialized out-of-process from the
same event stream, kept eventually consistent with the write side rather
than serving reads from it directly. Together they're this project's
worked example of CQRS (Command Query Responsibility Segregation) sitting
on top of event sourcing — the explicit third pillar of what this project
demonstrates, alongside event sourcing itself and business event streaming
(`README.md`).

If you haven't read `ADR-015` (why projections consume the public Follow
API rather than a private hook) and `ADR-016` (`ChangeKind`, and why the
merge rule lives in exactly one place) yet, read those first — this
document is the implementation detail underneath the decisions made there.

## `IProjection<TReadModel>` — what a projection author actually writes

A projection author never sees raw events, never sees `ChangeKind`, and
never writes merge logic. They write two things: which key an event
belongs to, and how to turn the current, fully-merged state for that key
into their read-model row.

```csharp
public interface IProjection<TReadModel> where TReadModel : class
{
    string Name { get; }                                  // identifies this projection's checkpoint + snapshot space
    IReadOnlyCollection<string> EventTypes { get; }        // which event types to Follow

    string GetKey(string eventType, JsonNode payload);     // e.g. payload["OrderId"] -- projection-defined, not global

    // mergedState is the CURRENT, fully-merged snapshot for this key, after
    // ProjectionHost has already applied this event per its ChangeKind
    // (ADR-016) -- Full replace or Partial merge-patch, already done.
    TReadModel Project(string key, JsonNode mergedState);
}
```

`Project` is a pure function of the merged state — it has no reason to be
anything else, and being pure makes it trivially unit-testable without a
database, a Follow connection, or a checkpoint in sight.

## `ProjectionHost` — the generic part, written once

`ProjectionHost` is a background service, one instance per registered
`IProjection<T>` (or one host process running several — see
`06-solution-structure.md`). Its loop, per projection:

1. Read `ProjectionCheckpoint.LastSequenceNumber` for this projection's
   `Name` (`0` if no row exists yet — first run).
2. `QUERY /follow/{event-type}` — one call per entry in `EventTypes`, or a
   single call if the Follow API is extended to accept multiple types in
   one connection (not otherwise required by this design; treat as an
   implementation detail) — with `mode=replay&fromSequenceNumber=<checkpoint>`
   (`ADR-015`: always replay, never tail).
3. For each streamed event:
   a. `key = projection.GetKey(eventType, payload)`.
   b. Load `ProjectionSnapshot` for `(ProjectionName, key)`, if any.
   c. Apply per the event type's registered `ChangeKind` (`ADR-016`):
      - `Full` → snapshot becomes `payload`, wholesale.
      - `Partial` → snapshot becomes `MergePatch(snapshot, payload)` — a
        field present in `payload` overwrites; a field **absent** from
        `payload` is left as-is in `snapshot`. This is the exact function
        `ADR-009`'s masking consumer-guidance already describes; it is not
        reimplemented differently here.
   d. Upsert the merged snapshot back to `ProjectionSnapshot`, and update
      `LastAppliedSequenceNumber` for that row.
   e. Call `projection.Project(key, mergedSnapshot)`, upsert the returned
      `TReadModel` into the projection's own read-model table, keyed by
      `key`.
   f. After the batch (or on each event, depending on desired durability
      granularity — not prescribed further here), advance
      `ProjectionCheckpoint.LastSequenceNumber` to the event's
      `SequenceNumber`.

```csharp
static JsonNode MergePatch(JsonNode? current, JsonNode incoming)
{
    if (current is not JsonObject baseObj) return incoming.DeepClone();
    if (incoming is not JsonObject patchObj) return incoming.DeepClone(); // Full, or a non-object payload
    var result = (JsonObject)baseObj.DeepClone();
    foreach (var (k, v) in patchObj)
        result[k] = v?.DeepClone();   // present in payload -> overwrite; absent keys in patchObj untouched in result
    return result;
}
```

(`MergePatch` above is only ever invoked for `Partial` events — `Full`
events skip straight to "snapshot becomes payload" and never call it.
Shown together here for contrast, not because the host branches into this
one function for both.)

## Checkpointing and rebuild

```csharp
public class ProjectionCheckpoint
{
    public string ProjectionName { get; set; } = default!;   // PK
    public long LastSequenceNumber { get; set; }
}

public class ProjectionSnapshot
{
    public string ProjectionName { get; set; } = default!;   // PK part 1
    public string Key { get; set; } = default!;              // PK part 2
    public string SnapshotJson { get; set; } = default!;
    public long LastAppliedSequenceNumber { get; set; }
}
```

**Incremental catch-up** (the normal case, including resuming after
downtime): reconnect with `mode=replay&fromSequenceNumber=<checkpoint>`.
Per `ADR-010`, this delivers exactly the matching history since the
checkpoint, then continues live, with no gap and no duplicate — the same
guarantee any Follow consumer gets, not something `ProjectionHost` adds.

**Full rebuild** (e.g. after a `Project` mapping changes and old read-model
rows are now the wrong shape): truncate this projection's read-model
table(s) and its `ProjectionSnapshot` rows, delete or zero its
`ProjectionCheckpoint` row, reconnect. This **is** the same code path as
incremental catch-up, starting from `0` — there is no separate "rebuild
mode" to test independently. Determinism (rebuilding produces the same
end state incremental application would have) follows directly from the
merge rule being a pure function of "all events for this key, in
`SequenceNumber` order" — replaying from `0` and replaying incrementally
apply the exact same sequence of `Full`-replace/`Partial`-merge steps, just
in different-sized batches.

## Read-side persistence

A dedicated `ProjectionsDbContext` (in `EventStore.Projections.Host`) owns
`ProjectionCheckpoint`, `ProjectionSnapshot`, and every registered
projection's read-model table(s) (e.g. `OrderSummary` — see
[`features/cqrs-projections.md`](features/cqrs-projections.md)). It is a
**separate database** from `EventStoreContext` — never the same connection
string, never a cross-database join — reachable from the write side only
by `ProjectionHost` acting as an ordinary HTTP client of the public Follow
API (`ADR-015`).

**No per-provider build split here, unlike `ADR-001`.** The write side
needs three `IJsonPathTranslator` implementations because it queries
generic JSON text (`Payload`) with provider-specific native JSON functions
(`04-odata-filter-pushdown.md`). The read side has no such problem: a read
model's columns are ordinary typed relational columns
(`OrderSummary.CustomerName nvarchar`, `OrderSummary.Status`, an enum
column, `OrderSummary.ShippedAt`, a datetime column) — there is no JSON
extraction at query time to translate per provider, because the whole
point of a CQRS read model is to already be shaped for its query, not
generic storage queried into shape. One EF Core provider (SQLite is enough
for this example) is sufficient; nothing about this design prevents a real
project from choosing differently per read model, since each projection's
`ProjectionsDbContext` usage is independent, but that's a deployment
choice this design doesn't need to make for you.

## Authentication

`ProjectionHost` is a Follow caller like any other (`ADR-015`), so it
needs its own OAuth2 client — a fourth seeded client alongside the three
in `ADR-006` (e.g. `projections-client`, scope `events:follow`) — see
[`features/auth.md`](features/auth.md)'s seeded-clients table, which must
be extended to include it. If a projection consumes an event type gated by
`RequiredReadClaim` (`ADR-008`), `projections-client`'s token needs that
claim too, exactly as any other follower would.

## Worked example

See [`features/cqrs-projections.md`](features/cqrs-projections.md) for a
concrete Orders domain carried end-to-end through this design: `Full` and
`Partial` event types, an `OrderSummary` read model, the sequence diagram
for one event's trip from Follow through the snapshot merge to the
upserted row, and the Gherkin scenarios a `Phase 9` implementation
(`08-build-plan.md`) is built against.

## Suggested References

- [Martin Fowler — Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html) / [CQRS](https://martinfowler.com/bliki/CQRS.html) — the two patterns this whole document sits on top of.
- [Greg Young — CQRS Documents](https://cqrs.files.wordpress.com/2010/11/cqrs_documents.pdf) (2010) — the pattern's origin text.
- [RFC 7396](https://datatracker.ietf.org/doc/html/rfc7396) — JSON Merge Patch, the exact semantics `SnapshotMerger`'s `Partial` case implements (see `ADR-016`'s closing note on the one deliberate divergence — no delete-on-`null`).
- [Azure Architecture Center — CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs) / [Event Sourcing pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing).

See `references.md` for the full bibliography, including real off-the-shelf
event-store products (EventStoreDB, Marten) this design deliberately
doesn't adopt — building the mechanism from scratch is the point of this
project (`README.md`), not a gap.
